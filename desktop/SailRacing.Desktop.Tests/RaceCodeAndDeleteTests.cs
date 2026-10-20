using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using SailRacing.Data;
using SailRacing.Desktop.Shell;
using SailRacing.Models;
using SailRacing.Services;
using SailRacing.ViewModels;
using Xunit;

namespace SailRacing.Desktop.Tests;

internal sealed class RecordingDialogs : IDialogService
{
    public bool Answer { get; set; } = true;
    public string? LastTitle { get; private set; }
    public string? LastMessage { get; private set; }
    public int Asked { get; private set; }

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel)
    {
        Asked++;
        LastTitle = title;
        LastMessage = message;
        return Task.FromResult(Answer);
    }

    public Task<string?> PromptAsync(string title, string message, string accept, string cancel, string? placeholder = null) =>
        Task.FromResult<string?>(null);
}

internal sealed class RecordingClipboard : IClipboardService
{
    public string? Text { get; private set; }

    public Task SetTextAsync(string text)
    {
        Text = text;
        return Task.CompletedTask;
    }
}

public class RaceCodeAndDeleteTests
{
    private static async Task Settle(int rounds = 20)
    {
        for (var i = 0; i < rounds; i++)
        {
            await Task.Delay(30);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private sealed record World(IServiceProvider Services, RecordingDialogs Dialogs, RecordingClipboard Clipboard, SilentSpeech Speech, Race WithCode, Race WithoutCode);

    private static async Task<World> BuildAsync(TempPaths paths)
    {
        var dialogs = new RecordingDialogs();
        var clipboard = new RecordingClipboard();
        var speech = new SilentSpeech();
        var services = App.BuildServices(paths, s =>
        {
            s.AddSingleton<ISpeechService>(speech);
            s.AddSingleton<IDialogService>(dialogs);
            s.AddSingleton<IClipboardService>(clipboard);
        });
        AppConfig.ServerBaseUrl = "http://127.0.0.1:9";

        var participants = services.GetRequiredService<IParticipantRepository>();
        var fleets = services.GetRequiredService<IFleetRepository>();
        var races = services.GetRequiredService<IRaceRepository>();

        var p = new Participant { Name = "Lemanja", Helm = "Eric", Tcf = 1.0 };
        await participants.SaveAsync(p);
        var fleet = new Fleet { Name = "Cruisers" };
        await fleets.SaveAsync(fleet, new[] { p.Id });

        async Task<Race> Seed(string name, string? code, RaceStatus status)
        {
            var aggregate = new RaceAggregate
            {
                Race = new Race { Name = name, FleetId = fleet.Id, Status = status, LapsDefault = 2, JoinCode = code, RemoteRaceId = code is null ? null : "x" },
                RaceParticipants = new() { new RaceParticipant { ParticipantId = p.Id, Laps = 2 } }
            };
            await races.SaveAggregateAsync(aggregate);
            return aggregate.Race;
        }

        var withCode = await Seed("Autumn Cup", "K7P2M", RaceStatus.Setup);
        var withoutCode = await Seed("Evening race", null, RaceStatus.Racing);
        return new World(services, dialogs, clipboard, speech, withCode, withoutCode);
    }

    private static IEnumerable<string> Texts(Control root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? string.Empty);

    [AvaloniaFact]
    public async Task The_race_list_shows_each_races_code_and_a_delete_button()
    {
        using var paths = new TempPaths();
        var w = await BuildAsync(paths);

        var screen = w.Services.GetRequiredService<ScreenFactory>().Create("races", new Dictionary<string, string>());
        var window = new Window { Width = 1000, Height = 700, Content = screen };
        window.Show();
        await Settle();

        var texts = Texts(screen).ToList();
        Assert.Contains("Race code K7P2M", texts);
        Assert.Contains(texts, t => t.StartsWith("Race code: not assigned yet"));
        Assert.Equal(2, screen.GetVisualDescendants().OfType<Button>().Count(b => (b.Content as string) == "Delete"));

        if ((Environment.GetEnvironmentVariable("SR_SCREENSHOTS") ?? "") is { Length: > 0 } shots)
        {
            Directory.CreateDirectory(shots);
            window.CaptureRenderedFrame()?.Save(Path.Combine(shots, "races-code-delete.png"));
        }

        window.Close();
    }

    [AvaloniaFact]
    public async Task Declining_the_question_keeps_the_race()
    {
        using var paths = new TempPaths();
        var w = await BuildAsync(paths);
        w.Dialogs.Answer = false;

        var vm = w.Services.GetRequiredService<RaceListViewModel>();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(w.WithCode);

        Assert.Equal(1, w.Dialogs.Asked);
        Assert.Equal(2, vm.Races.Count);
        Assert.Equal(2, (await w.Services.GetRequiredService<IRaceRepository>().GetAllAsync()).Count);
    }

    [AvaloniaFact]
    public async Task Deleting_removes_the_race_and_its_queued_updates_but_says_the_server_copy_stays()
    {
        using var paths = new TempPaths();
        var w = await BuildAsync(paths);
        var sync = w.Services.GetRequiredService<IRaceSyncService>();
        var boat = (await w.Services.GetRequiredService<IRaceRepository>().GetAggregateAsync(w.WithCode.Id))!.RaceParticipants.Single();

        // An entry waiting to be sent for each race (the server is unreachable, so they stay queued).
        await sync.QueueTimingAsync(w.WithCode.Id, new RaceParticipant { ParticipantId = boat.ParticipantId, LapsCompleted = 1 }, 1000);
        await sync.QueueTimingAsync(w.WithoutCode.Id, new RaceParticipant { ParticipantId = boat.ParticipantId, LapsCompleted = 1 }, 1000);

        var vm = w.Services.GetRequiredService<RaceListViewModel>();
        await vm.LoadCommand.ExecuteAsync(null);
        await vm.DeleteCommand.ExecuteAsync(w.WithCode);

        // The question explained what would happen.
        Assert.Contains("Autumn Cup", w.Dialogs.LastMessage);
        Assert.Contains("K7P2M", w.Dialogs.LastMessage);
        Assert.Contains("server", w.Dialogs.LastMessage);

        var item = Assert.Single(vm.Races);
        Assert.Equal("Evening race", item.Name);
        Assert.Null(await w.Services.GetRequiredService<IRaceRepository>().GetAggregateAsync(w.WithCode.Id));
        Assert.Contains("K7P2M", vm.StatusMessage);
        Assert.Contains("server", vm.StatusMessage);

        Assert.Empty(await sync.GetPendingTimingParticipantIdsAsync(w.WithCode.Id));      // gone with the race
        Assert.Single(await sync.GetPendingTimingParticipantIdsAsync(w.WithoutCode.Id));  // the other race's queue is untouched

        // A race the server never saw is deleted with a plainer question.
        await vm.DeleteCommand.ExecuteAsync(w.WithoutCode);
        Assert.Contains("in progress", w.Dialogs.LastMessage);
        Assert.Contains("not been sent to the server", w.Dialogs.LastMessage);
        Assert.Empty(vm.Races);
    }

    [AvaloniaFact]
    public async Task Deleting_stops_the_start_sequence_only_when_it_is_that_races()
    {
        using var paths = new TempPaths();
        var w = await BuildAsync(paths);
        var sequence = w.Services.GetRequiredService<IStartSequenceService>();
        var vm = w.Services.GetRequiredService<RaceListViewModel>();
        await vm.LoadCommand.ExecuteAsync(null);

        sequence.Start(DateTimeOffset.UtcNow.AddMinutes(10), w.WithCode.Id);
        Assert.True(sequence.IsRunning);

        // Deleting a different race leaves the running sequence alone.
        await vm.DeleteCommand.ExecuteAsync(w.WithoutCode);
        Assert.True(sequence.IsRunning, "another race's countdown must not be stopped");
        Assert.DoesNotContain("Start sequence cancelled", w.Speech.Spoken);

        // Deleting its own race stops it.
        await vm.DeleteCommand.ExecuteAsync(w.WithCode);
        Assert.False(sequence.IsRunning);
        Assert.Contains("Start sequence cancelled", w.Speech.Spoken);
    }

    [AvaloniaTheory]
    [InlineData("raceSetup")]
    [InlineData("startSequence")]
    [InlineData("timingSheet")]
    [InlineData("results")]
    public async Task Every_race_screen_shows_the_race_name_and_code_at_the_top_with_a_copy_button(string screenName)
    {
        using var paths = new TempPaths();
        var w = await BuildAsync(paths);

        var screen = w.Services.GetRequiredService<ScreenFactory>().Create(screenName, new Dictionary<string, string> { ["raceId"] = w.WithCode.Id });
        var window = new Window { Width = 1000, Height = 800, Content = screen };
        window.Show();
        await Settle();

        Assert.Contains("Autumn Cup  ·  Race code K7P2M", Texts(screen));
        var copy = screen.GetVisualDescendants().OfType<Button>().Single(b => (b.Content as string) == "Copy code");
        Assert.True(copy.IsVisible);

        var vm = (RaceViewModelBase)screen.DataContext!;
        await vm.CopyRaceCodeCommand.ExecuteAsync(null);
        Assert.Equal("K7P2M", w.Clipboard.Text);

        window.Close();
    }

    [AvaloniaFact]
    public async Task A_race_without_a_code_says_it_is_assigned_after_syncing_and_offers_no_copy()
    {
        using var paths = new TempPaths();
        var w = await BuildAsync(paths);

        var screen = w.Services.GetRequiredService<ScreenFactory>().Create("timingSheet", new Dictionary<string, string> { ["raceId"] = w.WithoutCode.Id });
        var window = new Window { Width = 1000, Height = 800, Content = screen };
        window.Show();
        await Settle();

        Assert.Contains("Evening race  ·  Race code: assigned once the race has synced to the server", Texts(screen));
        var copy = screen.GetVisualDescendants().OfType<Button>().Single(b => (b.Content as string) == "Copy code");
        Assert.False(copy.IsVisible);

        window.Close();
    }
}

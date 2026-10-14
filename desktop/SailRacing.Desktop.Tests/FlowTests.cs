using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using SailRacing.Data;
using SailRacing.Desktop.Shell;
using SailRacing.Desktop.Views;
using SailRacing.Models;
using SailRacing.Services;
using SailRacing.ViewModels;
using Xunit;

namespace SailRacing.Desktop.Tests;

internal sealed class SilentSpeech : ISpeechService
{
    public List<string> Spoken { get; } = new();
    public Task PrepareAsync(IEnumerable<string> phrases) => Task.CompletedTask;
    public Task SpeakAsync(string text, CancellationToken token)
    {
        Spoken.Add(text);
        return Task.CompletedTask;
    }
}

internal sealed class AlwaysYes : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) => Task.FromResult(true);
    public Task<string?> PromptAsync(string title, string message, string accept, string cancel, string? placeholder = null) => Task.FromResult<string?>(null);
}

public class FlowTests
{
    private static async Task Settle(int rounds = 20)
    {
        for (var i = 0; i < rounds; i++)
        {
            await Task.Delay(30);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static (IServiceProvider Services, SilentSpeech Speech) Build(TempPaths paths)
    {
        var speech = new SilentSpeech();
        var services = App.BuildServices(paths, s =>
        {
            s.AddSingleton<ISpeechService>(speech);
            s.AddSingleton<IDialogService, AlwaysYes>();
        });
        AppConfig.ServerBaseUrl = "http://127.0.0.1:9"; // nothing listens: the app must carry on offline
        return (services, speech);
    }

    private static async Task<string> SeedRaceAsync(IServiceProvider services, RaceStatus status, DateTimeOffset? startAt)
    {
        var participants = services.GetRequiredService<IParticipantRepository>();
        var fleets = services.GetRequiredService<IFleetRepository>();
        var races = services.GetRequiredService<IRaceRepository>();

        var p = new Participant { Name = "Lemanja", Helm = "Eric", Tcf = 1.0 };
        await participants.SaveAsync(p);
        var fleet = new Fleet { Name = "Cruisers" };
        await fleets.SaveAsync(fleet, new[] { p.Id });

        var aggregate = new RaceAggregate
        {
            Race = new Race { Name = "Test race", FleetId = fleet.Id, Status = status, LapsDefault = 2, StartAt = startAt },
            RaceParticipants = new() { new RaceParticipant { ParticipantId = p.Id, Laps = 2 } }
        };
        await races.SaveAggregateAsync(aggregate);
        return aggregate.Race.Id;
    }

    [AvaloniaFact]
    public async Task Navigation_follows_the_same_routes_as_the_maui_app()
    {
        using var paths = new TempPaths();
        var (services, _) = Build(paths);
        var raceId = await SeedRaceAsync(services, RaceStatus.Setup, null);

        var navigator = services.GetRequiredService<ShellNavigator>();
        var window = new MainWindow(services.GetRequiredService<ScreenFactory>(), navigator);
        window.Show();
        await Settle();

        var host = window.FindControl<ContentControl>("Host")!;
        Assert.IsType<CompetitorsView>(host.Content);
        Assert.True(window.FindControl<RadioButton>("TabCompetitors")!.IsChecked);

        await navigator.GoToAsync($"raceSetup?raceId={raceId}");
        await Settle();
        Assert.IsType<RaceSetupView>(host.Content);
        Assert.Equal(raceId, ((RaceSetupViewModel)((RaceSetupView)host.Content!).DataContext!).RaceId);
        Assert.True(window.FindControl<RadioButton>("TabRace")!.IsChecked, "race screens belong to the Race tab");

        await navigator.GoToAsync($"startSequence?raceId={raceId}");
        await Settle();
        Assert.IsType<StartSequenceView>(host.Content);

        await navigator.GoToAsync($"timingSheet?raceId={raceId}");
        await Settle();
        Assert.IsType<TimingSheetView>(host.Content);

        await navigator.GoToAsync($"results?raceId={raceId}");
        await Settle();
        Assert.IsType<ResultsView>(host.Content);

        await navigator.GoToAsync("//races");
        await Settle();
        Assert.IsType<RaceListView>(host.Content);

        window.Close();
    }

    [AvaloniaFact]
    public async Task A_start_sequence_can_begin_and_then_be_cancelled_back_to_the_countdown()
    {
        using var paths = new TempPaths();
        var (services, speech) = Build(paths);
        var raceId = await SeedRaceAsync(services, RaceStatus.Setup, null);
        var races = services.GetRequiredService<IRaceRepository>();

        var screen = services.GetRequiredService<ScreenFactory>().Create("startSequence", new Dictionary<string, string> { ["raceId"] = raceId });
        var window = new Window { Width = 900, Height = 700, Content = screen };
        window.Show();
        await Settle();

        var vm = (StartSequenceViewModel)screen.DataContext!;
        Assert.False(vm.IsSequenceStarted);
        Assert.False(vm.CanCancelSequence);

        // Begin: the sequence starts, T0 is stored, and (with the standard 5-4-1-0 sequence) is 10 minutes away.
        await vm.BeginSequenceCommand.ExecuteAsync(null);
        await Settle();
        Assert.True(vm.IsSequenceStarted);
        Assert.True(vm.CanCancelSequence);
        Assert.StartsWith("-10:0", vm.CountdownText);
        var stored = (await races.GetAggregateAsync(raceId))!.Race;
        Assert.NotNull(stored.StartAt);
        Assert.Equal(RaceStatus.StartSequence, stored.Status);

        // The flags rise when the view model says they are up, and come down again.
        var flags = screen.GetVisualDescendants().OfType<StackPanel>().Where(p => p.Classes.Contains("flag")).ToList();
        Assert.Equal(2, flags.Count);
        vm.ClassFlagUp = true;
        await Settle(5);
        Assert.Contains("up", flags[0].Classes);
        Assert.DoesNotContain("up", flags[1].Classes);
        vm.ClassFlagUp = false;
        await Settle(5);
        Assert.DoesNotContain("up", flags[0].Classes);

        // Cancel: back to the pre-start screen, start time cleared, announced, and no announcement left running.
        await vm.CancelSequenceCommand.ExecuteAsync(null);
        await Settle();
        Assert.False(vm.IsSequenceStarted);
        Assert.False(vm.IsSequenceComplete);
        Assert.Equal("--:--", vm.CountdownText);
        var after = (await races.GetAggregateAsync(raceId))!.Race;
        Assert.Null(after.StartAt);
        Assert.Equal(RaceStatus.Setup, after.Status);
        Assert.Contains("Start sequence cancelled", speech.Spoken);

        // ... and a new sequence can begin straight away.
        await vm.BeginSequenceCommand.ExecuteAsync(null);
        await Settle();
        Assert.True(vm.IsSequenceStarted);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Manual_timing_entries_update_the_sheet_and_are_queued_for_the_server()
    {
        using var paths = new TempPaths();
        var (services, _) = Build(paths);
        var raceId = await SeedRaceAsync(services, RaceStatus.Racing, DateTimeOffset.UtcNow.AddMinutes(-20));

        var screen = services.GetRequiredService<ScreenFactory>().Create("timingSheet", new Dictionary<string, string> { ["raceId"] = raceId });
        var window = new Window { Width = 900, Height = 700, Content = screen };
        window.Show();
        await Settle();

        var vm = (TimingSheetViewModel)screen.DataContext!;
        var entry = Assert.Single(vm.Entries);

        await vm.RecordLapNowCommand.ExecuteAsync(entry);
        Assert.Equal(1, entry.LapsCompleted);
        Assert.True(entry.IsOnFinalLap);

        await vm.RecordLapNowCommand.ExecuteAsync(entry);
        Assert.Equal(RaceParticipantStatus.Finished, entry.Status);
        Assert.False(entry.CanRecordLap);

        // The server is unreachable (nothing listens), so the entry waits in the local queue.
        await Settle(10);
        var pending = await services.GetRequiredService<IRaceSyncService>().GetPendingTimingParticipantIdsAsync(raceId);
        Assert.Contains(entry.ParticipantId, pending);

        var stored = (await services.GetRequiredService<IRaceRepository>().GetAggregateAsync(raceId))!;
        Assert.Equal(RaceParticipantStatus.Finished, stored.RaceParticipants.Single().Status);

        window.Close();
    }
}

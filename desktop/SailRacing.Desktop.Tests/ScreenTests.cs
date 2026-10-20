using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SailRacing.Data;
using SailRacing.Desktop;
using SailRacing.Desktop.Shell;
using SailRacing.Desktop.Views;
using SailRacing.Models;
using SailRacing.Services;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(SailRacing.Desktop.Tests.TestAppBuilder))]

// The app keeps its settings in a static store (AppConfig); tests that each install their own must not overlap.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SailRacing.Desktop.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .WithInterFont()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Collects Avalonia's binding/layout warnings, so a screen with a wrong binding fails its test.</summary>
internal sealed class CapturingSink : ILogSink
{
    public List<string> Problems { get; } = new();

    public bool IsEnabled(LogEventLevel level, string area) =>
        level >= LogEventLevel.Warning && area is "Binding" or "Property" or "Layout" or "Control" or "Styling";

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Problems.Add($"[{area}] {messageTemplate}");

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        var text = messageTemplate;
        foreach (var v in propertyValues)
        {
            var i = text.IndexOf('{');
            var j = text.IndexOf('}', i < 0 ? 0 : i);
            if (i >= 0 && j > i) text = text[..i] + v + text[(j + 1)..];
        }

        Problems.Add($"[{area}] {text}");
    }
}

/// <summary>A temporary data folder for the app under test.</summary>
internal sealed class TempPaths : IAppPaths, IDisposable
{
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "sr-desktop-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        try { Directory.Delete(DataDirectory, true); } catch { /* temp folder */ }
    }
}

public class ScreenTests
{
    private static readonly string Shots = Environment.GetEnvironmentVariable("SR_SCREENSHOTS") ?? string.Empty;

    private static async Task<(IServiceProvider Services, string RaceId)> SeedAsync(TempPaths paths)
    {
        var services = App.BuildServices(paths);

        var participants = services.GetRequiredService<IParticipantRepository>();
        var fleets = services.GetRequiredService<IFleetRepository>();
        var races = services.GetRequiredService<IRaceRepository>();

        var ids = new List<string>();
        foreach (var (name, helm, tcf) in new[] { ("Lemanja", "Eric Henry", 1.000), ("Bise", "Anna Keller", 0.912), ("Joran", "Luc Favre", 1.084) })
        {
            var p = new Participant { Name = name, Helm = helm, Tcf = tcf };
            await participants.SaveAsync(p);
            ids.Add(p.Id);
        }

        var fleet = new Fleet { Name = "Cruisers A" };
        await fleets.SaveAsync(fleet, ids);

        var start = DateTimeOffset.UtcNow.AddMinutes(-40);
        var aggregate = new RaceAggregate
        {
            Race = new Race
            {
                Name = "Autumn Cup", FleetId = fleet.Id, Status = RaceStatus.Racing, LapsDefault = 3,
                StartAt = start, ScheduledStartTime = start, JoinCode = "K7P2M", RemoteRaceId = "x", SyncStatus = SyncStatus.Synced
            },
            StartLine = new StartLine { CommitteeLatitude = 46.454, CommitteeLongitude = 6.55, PinLatitude = 46.454, PinLongitude = 6.553 },
            Buoys = new() { new Buoy { Name = "Windward", Sequence = 1, Latitude = 46.46, Longitude = 6.5515 } },
            RaceParticipants = new()
            {
                new RaceParticipant { ParticipantId = ids[0], Laps = 3, LapsCompleted = 3, Status = RaceParticipantStatus.Finished, FinishTime = start.AddMinutes(33), ElapsedSeconds = 1980 },
                new RaceParticipant { ParticipantId = ids[1], Laps = 3, LapsCompleted = 2, IsOnFinalLap = true },
                new RaceParticipant { ParticipantId = ids[2], Laps = 3, LapsCompleted = 1, Status = RaceParticipantStatus.Dnf }
            }
        };
        await races.SaveAggregateAsync(aggregate);
        return (services, aggregate.Race.Id);
    }

    /// <summary>Shows a screen in a window of its own, lets it load and lay out, and returns it with any problems found.</summary>
    private static async Task<(Window Window, ScreenView? Screen)> ShowAsync(IServiceProvider services, string name, string? raceId, string shot)
    {
        var query = raceId is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["raceId"] = raceId };
        var screen = services.GetRequiredService<ScreenFactory>().Create(name, query);

        var window = new Window { Width = 1000, Height = 760, Content = screen };
        window.Show();

        // Let the load tasks (database reads, async view-model setup) finish and the bindings settle.
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        window.UpdateLayout();
        if (Shots.Length > 0)
        {
            Directory.CreateDirectory(Shots);
            window.CaptureRenderedFrame()?.Save(Path.Combine(Shots, shot + ".png"));
        }

        return (window, screen);
    }

    [AvaloniaTheory]
    [InlineData("competitors", false)]
    [InlineData("fleets", false)]
    [InlineData("races", false)]
    [InlineData("settings", false)]
    [InlineData("raceSetup", true)]
    [InlineData("startSequence", true)]
    [InlineData("timingSheet", true)]
    [InlineData("results", true)]
    public async Task Every_screen_renders_with_real_data_and_no_binding_errors(string screen, bool needsRace)
    {
        var sink = new CapturingSink();
        Logger.Sink = sink;

        using var paths = new TempPaths();
        var (services, raceId) = await SeedAsync(paths);
        var (window, view) = await ShowAsync(services, screen, needsRace ? raceId : null, screen);

        Assert.NotNull(view);
        Assert.True(window.Bounds.Width > 0 && view!.Bounds.Height > 0, "the screen has no size");
        Assert.True(sink.Problems.Count == 0, "Avalonia reported problems:\n" + string.Join("\n", sink.Problems.Distinct()));

        window.Close();
    }
}

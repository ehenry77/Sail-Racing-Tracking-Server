using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using SailRacing.Data;
using SailRacing.Desktop.Platform;
using SailRacing.Desktop.Shell;
using SailRacing.Desktop.Views;
using SailRacing.Models;
using SailRacing.Services;
using SailRacing.ViewModels;
using Xunit;

namespace SailRacing.Desktop.Tests;

/// <summary>Records nothing, but writes a small file when it stops, as a real recording would leave.</summary>
internal sealed class FakeVideoRecorder : IVideoRecorder
{
    public bool IsRecording { get; private set; }
    public int Starts { get; private set; }
    public int Stops { get; private set; }
    public string? File { get; private set; }
    public bool Sound { get; set; } = true;
    public Exception? StartFailure { get; set; }

    public Task PrepareAsync(string? videoDeviceId, string? audioDeviceId, bool withSound) => Task.CompletedTask;
    public Task ReleaseAsync() => Task.CompletedTask;

    public Task<VideoStarted> StartAsync(string filePath, string? videoDeviceId, string? audioDeviceId, bool withSound, VideoOverlay? overlay)
    {
        if (StartFailure is not null) return Task.FromException<VideoStarted>(StartFailure);
        Starts++;
        IsRecording = true;
        File = filePath;
        return Task.FromResult(new VideoStarted(withSound && Sound, HasOverlay: overlay is not null));
    }

    public Task StopAsync()
    {
        Stops++;
        IsRecording = false;
        if (File is not null) System.IO.File.WriteAllText(File, "video");
        return Task.CompletedTask;
    }
}

public class VideoFlowTests
{
    private static async Task Settle(int rounds = 20)
    {
        for (var i = 0; i < rounds; i++)
        {
            await Task.Delay(30);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static async Task<bool> Until(Func<bool> condition, int timeoutMs = 5000)
    {
        for (var waited = 0; waited < timeoutMs; waited += 30)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition()) return true;
            await Task.Delay(30);
        }

        return condition();
    }

    private sealed record World(IServiceProvider Services, FakeVideoRecorder Recorder, RecordingDialogs Dialogs, string RaceId);

    private static async Task<World> BuildAsync(TempPaths paths, DateTimeOffset? scheduledStart = null)
    {
        var recorder = new FakeVideoRecorder();
        var dialogs = new RecordingDialogs();
        var services = App.BuildServices(paths, s =>
        {
            s.AddSingleton<ISpeechService>(new SilentSpeech());
            s.AddSingleton<IDialogService>(dialogs);
            s.AddSingleton<IVideoRecorder>(recorder);
            s.AddSingleton<IFileService>(new TempDocuments());
        });
        AppConfig.ServerBaseUrl = "http://127.0.0.1:9";
        AppConfig.RecordStartVideo = true;
        AppConfig.RecordStartVideoSound = true;

        var p = new Participant { Name = "Lemanja", Helm = "Eric", Tcf = 1.0 };
        await services.GetRequiredService<IParticipantRepository>().SaveAsync(p);
        var fleet = new Fleet { Name = "Cruisers" };
        await services.GetRequiredService<IFleetRepository>().SaveAsync(fleet, new[] { p.Id });

        var aggregate = new RaceAggregate
        {
            Race = new Race { Name = "Autumn Cup", FleetId = fleet.Id, LapsDefault = 2, ScheduledStartTime = scheduledStart },
            RaceParticipants = new() { new RaceParticipant { ParticipantId = p.Id, Laps = 2 } }
        };
        await services.GetRequiredService<IRaceRepository>().SaveAggregateAsync(aggregate);
        return new World(services, recorder, dialogs, aggregate.Race.Id);
    }

    private static async Task<(Window Window, ScreenView Screen, StartSequenceViewModel Vm)> OpenAsync(World w)
    {
        var screen = w.Services.GetRequiredService<ScreenFactory>().Create("startSequence", new Dictionary<string, string> { ["raceId"] = w.RaceId });
        var window = new Window { Width = 1000, Height = 800, Content = screen };
        window.Show();
        await Settle();
        return (window, (ScreenView)screen, (StartSequenceViewModel)screen.DataContext!);
    }

    [AvaloniaFact]
    public async Task Beginning_the_sequence_lines_the_video_up_for_the_last_minute_and_all_clear_drops_it_if_it_never_began()
    {
        using var paths = new TempPaths();
        var w = await BuildAsync(paths);
        var (window, screen, vm) = await OpenAsync(w);
        var video = w.Services.GetRequiredService<IStartVideoService>();

        Assert.Equal("Record video now", vm.VideoButtonText);

        await vm.BeginSequenceCommand.ExecuteAsync(null);   // standard sequence: the start is 10 minutes away
        await Settle();

        var startAt = (await w.Services.GetRequiredService<IRaceRepository>().GetAggregateAsync(w.RaceId))!.Race.StartAt!.Value;
        Assert.True(video.IsScheduled);
        Assert.Equal(startAt - TimeSpan.FromMinutes(1), video.RecordingStartsAt);
        Assert.False(video.IsRecording, "nothing is recorded until the last minute");
        Assert.True(vm.IsVideoScheduled);
        Assert.Contains("recording starts at", vm.VideoStatusText);
        Assert.Contains("one minute before the start", vm.VideoStatusText);
        Assert.Equal("Cancel the video", vm.VideoButtonText);
        Assert.Equal(0, w.Recorder.Starts);

        // The line and button are on the screen.
        Assert.Contains(screen.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").Contains("recording starts at"));
        Assert.Contains(screen.GetVisualDescendants().OfType<Button>(), b => (b.Content as string) == "Cancel the video");

        await vm.AllClearCommand.ExecuteAsync(null);
        await Settle();
        Assert.False(video.IsScheduled);
        Assert.Equal(0, w.Recorder.Starts);

        window.Close();
    }

    [AvaloniaFact]
    public async Task With_the_last_minute_already_here_recording_starts_at_once_and_runs_until_all_clear()
    {
        using var paths = new TempPaths();
        var w = await BuildAsync(paths, scheduledStart: DateTimeOffset.UtcNow.AddSeconds(20));
        var (window, screen, vm) = await OpenAsync(w);
        var video = w.Services.GetRequiredService<IStartVideoService>();

        await vm.BeginSequenceCommand.ExecuteAsync(null);    // a scheduled start 20 s away: inside the last minute

        Assert.True(await Until(() => vm.IsRecordingVideo), "recording should have started: " + vm.VideoStatusText);
        Assert.Equal(1, w.Recorder.Starts);
        Assert.StartsWith("● Recording the start", vm.VideoStatusText);
        Assert.DoesNotContain("no sound", vm.VideoStatusText);
        Assert.Equal("Stop recording", vm.VideoButtonText);
        var file = video.CurrentFile!;
        Assert.Matches(@"Sail-Racing videos[\\/]Autumn Cup[\\/]Autumn Cup start \d{8}-\d{6}\.mp4$", file);

        await Settle(40); // let the screen redraw before it is photographed
        if ((Environment.GetEnvironmentVariable("SR_SCREENSHOTS") ?? "") is { Length: > 0 } shots)
        {
            Directory.CreateDirectory(shots);
            window.CaptureRenderedFrame()?.Save(Path.Combine(shots, "startSequence-recording.png"));
        }

        await vm.AllClearCommand.ExecuteAsync(null);         // the video ends at All Clear
        Assert.True(await Until(() => !video.IsRecording));
        Assert.Equal(1, w.Recorder.Stops);
        Assert.True(File.Exists(file));

        var stored = (await w.Services.GetRequiredService<IRaceRepository>().GetAggregateAsync(w.RaceId))!.Race;
        Assert.Equal(file, stored.StartVideoPath);
        Assert.Equal(RaceStatus.Racing, stored.Status);

        window.Close();

        // Back on the screen later: the finished video can be opened from it.
        var (window2, screen2, vm2) = await OpenAsync(w);
        Assert.True(vm2.HasStartVideo);
        Assert.Equal(file, vm2.StartVideoPath);
        window2.Close();
    }

    [AvaloniaFact]
    public async Task The_recording_carries_on_when_the_screen_is_left_and_is_shown_again_on_return()
    {
        using var paths = new TempPaths();
        var w = await BuildAsync(paths, scheduledStart: DateTimeOffset.UtcNow.AddSeconds(20));
        var (window, screen, vm) = await OpenAsync(w);
        var video = w.Services.GetRequiredService<IStartVideoService>();

        await vm.BeginSequenceCommand.ExecuteAsync(null);
        Assert.True(await Until(() => video.IsRecording));

        window.Close();                                       // leave the screen
        await Settle();
        Assert.True(video.IsRecording, "leaving the screen must not stop the recording");
        Assert.Equal(0, w.Recorder.Stops);

        var (window2, _, vm2) = await OpenAsync(w);           // come back
        Assert.True(vm2.IsRecordingVideo);
        Assert.StartsWith("● Recording the start", vm2.VideoStatusText);

        await video.StopAsync();
        window2.Close();
    }

    [AvaloniaFact]
    public async Task Cancelling_the_sequence_stops_and_keeps_a_recording_and_drops_a_waiting_one()
    {
        using var paths = new TempPaths();
        var w = await BuildAsync(paths, scheduledStart: DateTimeOffset.UtcNow.AddSeconds(30));
        var (window, _, vm) = await OpenAsync(w);
        var video = w.Services.GetRequiredService<IStartVideoService>();

        await vm.BeginSequenceCommand.ExecuteAsync(null);
        Assert.True(await Until(() => video.IsRecording));
        var file = video.CurrentFile!;

        await vm.CancelSequenceCommand.ExecuteAsync(null);    // the dialog answers yes
        Assert.True(await Until(() => !video.IsRecording));
        Assert.True(File.Exists(file), "the recording is kept");
        Assert.False(vm.IsSequenceStarted);

        window.Close();
    }

    [AvaloniaFact]
    public async Task With_the_setting_off_beginning_the_sequence_records_nothing_but_the_button_still_works()
    {
        using var paths = new TempPaths();
        var w = await BuildAsync(paths, scheduledStart: DateTimeOffset.UtcNow.AddSeconds(20));
        AppConfig.RecordStartVideo = false;
        var (window, _, vm) = await OpenAsync(w);
        var video = w.Services.GetRequiredService<IStartVideoService>();

        await vm.BeginSequenceCommand.ExecuteAsync(null);
        await Settle(30);
        Assert.False(video.IsScheduled);
        Assert.False(video.IsRecording);

        await vm.ToggleStartVideoCommand.ExecuteAsync(null);  // by hand: start now
        Assert.True(await Until(() => vm.IsRecordingVideo));
        Assert.Equal("Stop recording", vm.VideoButtonText);

        await vm.ToggleStartVideoCommand.ExecuteAsync(null);  // and stop
        Assert.True(await Until(() => !vm.IsRecordingVideo));
        Assert.True(vm.HasStartVideo);
        Assert.Equal("Record video now", vm.VideoButtonText);

        window.Close();
    }

    [AvaloniaFact]
    public async Task A_camera_that_cannot_record_is_explained_on_the_screen_and_the_sequence_carries_on()
    {
        using var paths = new TempPaths();
        var w = await BuildAsync(paths, scheduledStart: DateTimeOffset.UtcNow.AddSeconds(20));
        w.Recorder.StartFailure = new InvalidOperationException("no camera was found");
        var (window, _, vm) = await OpenAsync(w);

        await vm.BeginSequenceCommand.ExecuteAsync(null);

        Assert.True(await Until(() => vm.VideoStatusText.Contains("no camera was found")), vm.VideoStatusText);
        Assert.False(vm.IsRecordingVideo);
        Assert.True(vm.IsSequenceStarted);          // the start itself is unaffected
        Assert.Equal("Record video now", vm.VideoButtonText);

        window.Close();
    }

    [AvaloniaFact]
    public async Task Deleting_the_race_that_is_being_recorded_stops_the_recording()
    {
        using var paths = new TempPaths();
        var w = await BuildAsync(paths);
        var video = w.Services.GetRequiredService<IStartVideoService>();
        var races = w.Services.GetRequiredService<IRaceRepository>();
        var race = (await races.GetAggregateAsync(w.RaceId))!.Race;

        await video.StartAsync(race.Id, race.Name);
        Assert.True(video.IsRecording);

        var list = w.Services.GetRequiredService<RaceListViewModel>();
        await list.LoadCommand.ExecuteAsync(null);
        await list.DeleteCommand.ExecuteAsync(race);

        Assert.False(video.IsRecording);
        Assert.Equal(1, w.Recorder.Stops);
        Assert.Null(await races.GetAggregateAsync(w.RaceId));
    }

    [AvaloniaFact]
    public async Task The_settings_screen_remembers_the_video_choices()
    {
        using var paths = new TempPaths();
        var w = await BuildAsync(paths);
        AppConfig.RecordStartVideo = false;
        AppConfig.RecordStartVideoSound = true;

        var screen = w.Services.GetRequiredService<ScreenFactory>().Create("settings", new Dictionary<string, string>());
        var window = new Window { Width = 1000, Height = 900, Content = screen };
        window.Show();
        await Settle();

        var vm = (SettingsViewModel)screen.DataContext!;
        Assert.False(vm.RecordStartVideo);
        Assert.True(vm.RecordStartVideoSound);

        vm.RecordStartVideo = true;
        vm.RecordStartVideoSound = false;
        vm.SaveCommand.Execute(null);

        Assert.True(AppConfig.RecordStartVideo);
        Assert.False(AppConfig.RecordStartVideoSound);
        window.Close();
    }
}

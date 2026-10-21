using SailRacing.Data;
using SailRacing.Models;
using SailRacing.Services;
using Xunit;

namespace SailRacing.Core.Tests;

internal sealed class FakeRecorder : IVideoRecorder
{
    public bool IsRecording { get; private set; }
    public int Prepared { get; private set; }
    public int Released { get; private set; }
    public int Starts { get; private set; }
    public int Stops { get; private set; }
    public string? File { get; private set; }
    public string? VideoDevice { get; private set; }
    public string? AudioDevice { get; private set; }
    public bool SoundRequested { get; private set; }
    public bool SoundAvailable { get; set; } = true;
    public bool OverlayPossible { get; set; } = true;
    public VideoOverlay? Overlay { get; private set; }
    public string? Note { get; set; }
    public Exception? StartFailure { get; set; }
    public Exception? StopFailure { get; set; }
    public DateTimeOffset? StartedAt { get; private set; }

    public Task PrepareAsync(string? videoDeviceId, string? audioDeviceId, bool withSound)
    {
        Prepared++;
        return Task.CompletedTask;
    }

    public Task<VideoStarted> StartAsync(string filePath, string? videoDeviceId, string? audioDeviceId, bool withSound, VideoOverlay? overlay)
    {
        if (StartFailure is not null)
        {
            return Task.FromException<VideoStarted>(StartFailure);
        }

        Overlay = overlay;

        Starts++;
        IsRecording = true;
        File = filePath;
        VideoDevice = videoDeviceId;
        AudioDevice = audioDeviceId;
        SoundRequested = withSound;
        StartedAt = DateTimeOffset.UtcNow;
        return Task.FromResult(new VideoStarted(withSound && SoundAvailable, OverlayPossible && overlay is not null, Note));
    }

    public Task StopAsync()
    {
        Stops++;
        IsRecording = false;
        return StopFailure is null ? Task.CompletedTask : Task.FromException(StopFailure);
    }

    public Task ReleaseAsync()
    {
        Released++;
        return Task.CompletedTask;
    }
}

/// <summary>Races kept in memory — all the video service needs of the database is "load, change one field, save".</summary>
internal sealed class MemoryRaces : IRaceRepository
{
    private readonly Dictionary<string, RaceAggregate> _items = new();

    public MemoryRaces(params Race[] races)
    {
        foreach (var r in races) _items[r.Id] = new RaceAggregate { Race = r };
    }

    public Task<List<Race>> GetAllAsync() => Task.FromResult(_items.Values.Select(a => a.Race).ToList());
    public Task<RaceAggregate?> GetAggregateAsync(string raceId) => Task.FromResult(_items.GetValueOrDefault(raceId));
    public Task SaveAggregateAsync(RaceAggregate aggregate)
    {
        _items[aggregate.Race.Id] = aggregate;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Race race)
    {
        _items.Remove(race.Id);
        return Task.CompletedTask;
    }

    public Task SetStartVideoPathAsync(string raceId, string? path)
    {
        if (_items.TryGetValue(raceId, out var a)) a.Race.StartVideoPath = path;
        return Task.CompletedTask;
    }
}

public class StartVideoTests : IDisposable
{
    private readonly TempFiles _files = new();
    private readonly FakeRecorder _recorder = new();
    private readonly Race _race = new() { Name = "Autumn Cup" };
    private readonly MemoryRaces _races;
    private readonly ISettingsStore _previousStore = AppConfig.Store;

    // Short real timings, so the scheduling is exercised for real in a second or two.
    private static readonly StartVideoTimings Quick = new(RecordBeforeStart: TimeSpan.FromSeconds(1.2), PrepareBeforeRecording: TimeSpan.FromSeconds(0.4));

    public StartVideoTests()
    {
        AppConfig.Store = new InMemorySettingsStore();
        _races = new MemoryRaces(_race);
    }

    public void Dispose()
    {
        AppConfig.Store = _previousStore;
        _files.Dispose();
    }

    private readonly FakeLocation _location = new();

    private StartVideoService Service(StartVideoTimings? timings = null) => new(_recorder, _files, _races, _location, timings);

    private static async Task<bool> Until(Func<bool> condition, int timeoutMs = 5000)
    {
        for (var waited = 0; waited < timeoutMs; waited += 25)
        {
            if (condition()) return true;
            await Task.Delay(25);
        }

        return condition();
    }

    [Fact]
    public async Task A_video_is_waiting_for_the_last_minute_and_can_be_cancelled()
    {
        var service = Service();
        var startAt = DateTimeOffset.UtcNow.AddMinutes(10);

        service.Schedule(_race.Id, _race.Name, startAt);

        Assert.True(service.IsScheduled);
        Assert.Equal(_race.Id, service.ScheduledRaceId);
        Assert.Equal(startAt - TimeSpan.FromMinutes(1), service.RecordingStartsAt);
        Assert.False(service.IsRecording);
        Assert.Contains("one minute before the start", service.LastMessage);
        Assert.Equal(_race.Id, service.MessageRaceId);

        await service.CancelScheduledAsync();

        Assert.False(service.IsScheduled);
        Assert.Equal(0, _recorder.Starts);
        Assert.Equal(1, _recorder.Released);
    }

    [Fact]
    public async Task Recording_starts_in_the_last_minute_with_the_camera_switched_on_just_before()
    {
        var service = Service(Quick);
        var changes = 0;
        service.StateChanged += (_, _) => Interlocked.Increment(ref changes);

        // The start is 2.5 s away: the camera is prepared at +0.9 s, recording begins at +1.3 s.
        var startAt = DateTimeOffset.UtcNow.AddSeconds(2.5);
        var scheduledAt = DateTimeOffset.UtcNow;
        service.Schedule(_race.Id, _race.Name, startAt);

        Assert.False(service.IsRecording);
        Assert.True(await Until(() => service.IsRecording));

        Assert.Equal(1, _recorder.Prepared);
        Assert.Equal(1, _recorder.Starts);
        Assert.False(service.IsScheduled);
        var recordingBegan = _recorder.StartedAt!.Value;
        Assert.InRange((recordingBegan - scheduledAt).TotalSeconds, 1.0, 2.2);      // not at once, and not late
        Assert.True(recordingBegan < startAt, "recording must begin before the start");
        Assert.Equal(_race.Id, service.RaceId);
        Assert.True(service.HasSound);

        // The file goes under Documents/Sail-Racing videos/<race>/ and is named by race and time (24 h).
        Assert.Equal(Path.Combine(_files.DocumentsDirectory, "Sail-Racing videos", "Autumn Cup"), Path.GetDirectoryName(service.CurrentFile));
        Assert.Matches(@"^Autumn Cup start \d{8}-\d{6}\.mp4$", Path.GetFileName(service.CurrentFile));
        Assert.True(changes >= 2, "scheduled and started should both have been announced");
    }

    [Fact]
    public async Task With_less_than_a_minute_left_it_starts_at_once()
    {
        var service = Service(); // the real 63 s lead
        var scheduledAt = DateTimeOffset.UtcNow;

        service.Schedule(_race.Id, _race.Name, scheduledAt.AddSeconds(20));

        Assert.True(await Until(() => service.IsRecording, 2000));
        Assert.True((_recorder.StartedAt!.Value - scheduledAt).TotalSeconds < 1.5);
    }

    [Fact]
    public async Task All_clear_stops_the_recording_saves_the_file_with_the_race_and_is_safe_to_repeat()
    {
        var service = Service();
        service.Schedule(_race.Id, _race.Name, DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.True(await Until(() => service.IsRecording));
        var file = service.CurrentFile!;

        var stopped = await service.StopAsync();

        Assert.True(stopped.Success, stopped.Message);
        Assert.Equal(file, stopped.Path);
        Assert.StartsWith("Video saved: ", stopped.Message);
        Assert.False(service.IsRecording);
        Assert.Equal(file, service.LastFile);
        Assert.Equal(_race.Id, service.LastRaceId);
        Assert.Equal(1, _recorder.Stops);
        Assert.Equal(file, (await _races.GetAggregateAsync(_race.Id))!.Race.StartVideoPath);

        var again = await service.StopAsync();
        Assert.True(again.Success);
        Assert.Equal(1, _recorder.Stops); // nothing more to stop
    }

    [Fact]
    public async Task Pressing_all_clear_before_the_last_minute_drops_the_video_instead_of_recording_later()
    {
        var service = Service(Quick);
        service.Schedule(_race.Id, _race.Name, DateTimeOffset.UtcNow.AddSeconds(2));

        await service.StopAsync();
        await Task.Delay(2500); // past the moment it would have started

        Assert.False(service.IsRecording);
        Assert.Equal(0, _recorder.Starts);
    }

    [Fact]
    public async Task Cancelling_the_sequence_stops_a_recording_in_progress_and_keeps_the_file()
    {
        var service = Service();
        service.Schedule(_race.Id, _race.Name, DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.True(await Until(() => service.IsRecording));

        await service.StopAsync();

        Assert.NotNull(service.LastFile);
        Assert.NotNull((await _races.GetAggregateAsync(_race.Id))!.Race.StartVideoPath);
    }

    [Fact]
    public async Task The_manual_button_starts_now_replaces_a_waiting_video_and_refuses_a_second_recording()
    {
        var service = Service();
        service.Schedule(_race.Id, _race.Name, DateTimeOffset.UtcNow.AddMinutes(10));

        var first = await service.StartAsync(_race.Id, _race.Name);
        Assert.True(first.Success, first.Message);
        Assert.False(service.IsScheduled);
        Assert.True(service.IsRecording);

        var second = await service.StartAsync(_race.Id, _race.Name);
        Assert.False(second.Success);
        Assert.Contains("already", second.Message);
        Assert.Equal(1, _recorder.Starts);

        // Scheduling while recording changes nothing.
        service.Schedule(_race.Id, _race.Name, DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.False(service.IsScheduled);
    }

    [Fact]
    public async Task The_chosen_camera_and_microphone_and_the_sound_setting_reach_the_recorder()
    {
        AppConfig.PreferredVideoInputDevice = ("cam-2", "Deck camera");
        AppConfig.PreferredAudioInputDevice = ("mic-1", "Cockpit mic");
        var service = Service();

        await service.StartAsync(_race.Id, _race.Name);

        Assert.Equal("cam-2", _recorder.VideoDevice);
        Assert.Equal("mic-1", _recorder.AudioDevice);
        Assert.True(_recorder.SoundRequested);

        await service.StopAsync();
        AppConfig.RecordStartVideoSound = false;
        var silent = await service.StartAsync(_race.Id, _race.Name);
        Assert.False(_recorder.SoundRequested);
        Assert.Contains("no sound", silent.Message);
        Assert.False(service.HasSound);
    }

    [Fact]
    public async Task A_missing_microphone_still_records_and_says_so()
    {
        _recorder.SoundAvailable = false;
        var service = Service();

        var result = await service.StartAsync(_race.Id, _race.Name);

        Assert.True(result.Success);
        Assert.True(service.IsRecording);
        Assert.False(service.HasSound);
        Assert.Contains("without sound", result.Message);
    }

    [Fact]
    public async Task A_camera_that_cannot_record_comes_back_as_a_message_and_leaves_nothing_running()
    {
        _recorder.StartFailure = new InvalidOperationException("Windows is not letting this app use the camera");
        var service = Service(Quick);

        var manual = await service.StartAsync(_race.Id, _race.Name);
        Assert.False(manual.Success);
        Assert.Contains("Windows is not letting this app use the camera", manual.Message);
        Assert.False(service.IsRecording);
        Assert.Equal(_race.Id, service.MessageRaceId);

        // The same failure at the scheduled moment is reported too, without an exception from a background task.
        service.Schedule(_race.Id, _race.Name, DateTimeOffset.UtcNow.AddSeconds(1.6));
        Assert.True(await Until(() => !service.IsScheduled && service.LastMessage.StartsWith("Could not record")));
        Assert.False(service.IsRecording);
    }

    [Fact]
    public async Task A_file_that_may_be_incomplete_is_reported_but_the_recorder_is_still_freed()
    {
        _recorder.StopFailure = new InvalidOperationException("the file may be incomplete");
        var service = Service();
        await service.StartAsync(_race.Id, _race.Name);

        var stopped = await service.StopAsync();

        Assert.False(stopped.Success);
        Assert.Contains("may be incomplete", stopped.Message);
        Assert.False(service.IsRecording);
        Assert.NotNull(service.LastFile);

        // and the next start works
        _recorder.StopFailure = null;
        Assert.True((await service.StartAsync(_race.Id, _race.Name)).Success);
    }

    [Fact]
    public void The_start_video_settings_default_to_off_with_sound_on_once_enabled()
    {
        Assert.False(AppConfig.RecordStartVideo);
        Assert.True(AppConfig.RecordStartVideoSound);

        AppConfig.RecordStartVideo = true;
        AppConfig.RecordStartVideoSound = false;
        Assert.True(AppConfig.RecordStartVideo);
        Assert.False(AppConfig.RecordStartVideoSound);
    }

    [Theory]
    [InlineData("Cup: A/B", "Cup_ A_B")]
    [InlineData("Race?*<>|\"", "Race______")]
    [InlineData("  trailing dots...  ", "trailing dots")]
    [InlineData("", "race")]
    [InlineData("...", "race")]
    public void Folder_names_are_safe_on_every_system(string name, string expected) =>
        Assert.Equal(expected, SafeFileName.Of(name, "race"));
}

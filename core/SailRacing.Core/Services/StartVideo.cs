using System.Diagnostics;
using System.Globalization;
using System.Text;
using SailRacing.Data;

namespace SailRacing.Services;

public sealed record StartVideoResult(bool Success, string Message, string? Path = null);

/// <summary>When the start video begins, relative to the start signal.</summary>
/// <param name="RecordBeforeStart">
/// How long before the start the recording begins. The video is meant to cover the last minute, so this is a minute
/// plus a few seconds' margin for the camera to produce its first frame.
/// </param>
/// <param name="PrepareBeforeRecording">How long before that the camera is switched on, so recording begins at once.</param>
/// <param name="GpsEvery">How often this PC's own GPS position is asked for while recording (10 s if not given).</param>
public sealed record StartVideoTimings(TimeSpan RecordBeforeStart, TimeSpan PrepareBeforeRecording, TimeSpan? GpsEvery = null)
{
    /// <summary>The last minute before the start is what is wanted; recording begins 3 s earlier to be sure of it.</summary>
    public static StartVideoTimings Default { get; } = new(TimeSpan.FromSeconds(63), TimeSpan.FromSeconds(12));

    /// <summary>The nominal point the video is "from": one minute before the start.</summary>
    public static TimeSpan LastMinute { get; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// The video of a start: recorded with the committee PC's camera (and microphone) from the last minute before the
/// start until "All Clear" is pressed. One service for the whole app, so the recording is started on time and carries
/// on while the committee moves between screens, and is stopped from wherever All Clear (or Cancel) is pressed.
/// </summary>
public interface IStartVideoService
{
    bool IsRecording { get; }

    /// <summary>The race being recorded, while recording.</summary>
    string? RaceId { get; }

    /// <summary>The file being written, while recording.</summary>
    string? CurrentFile { get; }

    DateTimeOffset? StartedAt { get; }

    /// <summary>Whether the microphone's sound is being recorded too (false if there is no microphone or it is blocked).</summary>
    bool HasSound { get; }

    /// <summary>Whether the time and GPS are drawn on the picture (not only kept in the subtitle file).</summary>
    bool HasOverlay { get; }

    /// <summary>True while a recording is waiting for the last minute to begin.</summary>
    bool IsScheduled { get; }

    /// <summary>The race the waiting recording is for.</summary>
    string? ScheduledRaceId { get; }

    /// <summary>The moment the waiting recording is "from" — one minute before the start signal.</summary>
    DateTimeOffset? RecordingStartsAt { get; }

    /// <summary>The most recent finished recording and its race.</summary>
    string? LastFile { get; }

    string? LastRaceId { get; }

    /// <summary>What happened last, in words for the committee ("Recording the start…", "Could not record…").</summary>
    string LastMessage { get; }

    /// <summary>The race <see cref="LastMessage"/> is about.</summary>
    string? MessageRaceId { get; }

    /// <summary>Raised when recording is scheduled, starts, stops or is cancelled. May be raised from any thread.</summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Arranges for the video to start in the last minute before <paramref name="startAt"/> and run until
    /// <see cref="StopAsync"/>. If less than that is left, it starts at once. Does nothing if already recording.
    /// </summary>
    void Schedule(string raceId, string raceName, DateTimeOffset startAt, GeoFix? raceLocation = null, string raceLocationLabel = "");

    /// <summary>Drops a waiting recording (and releases the camera if it had been switched on for it).</summary>
    Task CancelScheduledAsync();

    /// <summary>Starts recording right now (the manual button). Never throws: a problem comes back as a message.</summary>
    Task<StartVideoResult> StartAsync(string raceId, string raceName, GeoFix? raceLocation = null, string raceLocationLabel = "");

    /// <summary>Stops and saves the recording, and drops any waiting one. Never throws.</summary>
    Task<StartVideoResult> StopAsync();
}

public sealed class StartVideoService : IStartVideoService
{
    private readonly IVideoRecorder _recorder;
    private readonly IFileService _files;
    private readonly IRaceRepository _races;
    private readonly IGeolocationService _geolocation;
    private readonly StartVideoTimings _timings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _scheduleLock = new();
    private CancellationTokenSource? _schedule;
    private StampSession? _stamp;

    public StartVideoService(
        IVideoRecorder recorder, IFileService files, IRaceRepository races, IGeolocationService geolocation, StartVideoTimings? timings = null)
    {
        _geolocation = geolocation;
        _recorder = recorder;
        _files = files;
        _races = races;
        _timings = timings ?? StartVideoTimings.Default;
    }

    public bool IsRecording { get; private set; }

    public string? RaceId { get; private set; }

    public string? CurrentFile { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public bool HasSound { get; private set; }

    public bool HasOverlay { get; private set; }

    public bool IsScheduled { get; private set; }

    public string? ScheduledRaceId { get; private set; }

    public DateTimeOffset? RecordingStartsAt { get; private set; }

    public string? LastFile { get; private set; }

    public string? LastRaceId { get; private set; }

    public string LastMessage { get; private set; } = string.Empty;

    public string? MessageRaceId { get; private set; }

    public event EventHandler? StateChanged;

    private void Raise() => StateChanged?.Invoke(this, EventArgs.Empty);

    // ------------------------------------------------------------------------------------------ scheduling

    public void Schedule(string raceId, string raceName, DateTimeOffset startAt, GeoFix? raceLocation = null, string raceLocationLabel = "")
    {
        CancellationToken token;
        lock (_scheduleLock)
        {
            if (IsRecording)
            {
                return;
            }

            _schedule?.Cancel();
            _schedule = new CancellationTokenSource();
            token = _schedule.Token;
        }

        IsScheduled = true;
        ScheduledRaceId = raceId;
        RecordingStartsAt = startAt - StartVideoTimings.LastMinute;
        LastMessage = $"The video will start at {RecordingStartsAt.Value.ToLocalTime():HH:mm:ss}, one minute before the start.";
        MessageRaceId = raceId;
        Raise();

        _ = Task.Run(() => RunScheduleAsync(raceId, raceName, startAt, raceLocation, raceLocationLabel, token));
    }

    private async Task RunScheduleAsync(
        string raceId, string raceName, DateTimeOffset startAt, GeoFix? raceLocation, string raceLocationLabel, CancellationToken token)
    {
        try
        {
            // Switch the camera on a little ahead of time, so recording begins the moment it is due.
            await DelayUntilAsync(startAt - _timings.RecordBeforeStart - _timings.PrepareBeforeRecording, token);
            await _recorder.PrepareAsync(
                AppConfig.PreferredVideoInputDevice.Id, AppConfig.PreferredAudioInputDevice.Id, AppConfig.RecordStartVideoSound);

            await DelayUntilAsync(startAt - _timings.RecordBeforeStart, token);

            // The committee may have cancelled or pressed All Clear while we waited.
            token.ThrowIfCancellationRequested();
            ClearSchedule(token);
            await StartCoreAsync(raceId, raceName, raceLocation, raceLocationLabel);
        }
        catch (OperationCanceledException)
        {
            // Cancelled: whoever cancelled has already tidied up.
        }
        catch (Exception ex)
        {
            ClearSchedule(token);
            LastMessage = $"Could not record the video: {ex.Message}";
            MessageRaceId = raceId;
            Raise();
        }
    }

    private static async Task DelayUntilAsync(DateTimeOffset moment, CancellationToken token)
    {
        var wait = moment - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, token);
        }
    }

    private void ClearSchedule(CancellationToken token)
    {
        lock (_scheduleLock)
        {
            if (_schedule is { } current && current.Token == token)
            {
                _schedule = null;
            }
        }

        IsScheduled = false;
        ScheduledRaceId = null;
        RecordingStartsAt = null;
    }

    public async Task CancelScheduledAsync()
    {
        var wasScheduled = false;
        lock (_scheduleLock)
        {
            if (_schedule is not null)
            {
                _schedule.Cancel();
                _schedule = null;
                wasScheduled = true;
            }
        }

        if (!wasScheduled)
        {
            return;
        }

        IsScheduled = false;
        ScheduledRaceId = null;
        RecordingStartsAt = null;
        LastMessage = "The video was cancelled.";
        Raise();  // MessageRaceId stays as the scheduled race's

        if (!IsRecording)
        {
            try
            {
                await _recorder.ReleaseAsync();
            }
            catch
            {
                // nothing was recorded; releasing is only tidiness
            }
        }
    }

    // ------------------------------------------------------------------------------------------ recording

    public async Task<StartVideoResult> StartAsync(string raceId, string raceName, GeoFix? raceLocation = null, string raceLocationLabel = "")
    {
        await CancelScheduledAsync();
        return await StartCoreAsync(raceId, raceName, raceLocation, raceLocationLabel);
    }

    private async Task<StartVideoResult> StartCoreAsync(string raceId, string raceName, GeoFix? raceLocation, string raceLocationLabel)
    {
        await _gate.WaitAsync();
        try
        {
            if (IsRecording)
            {
                return new StartVideoResult(false, "A video is already being recorded.", CurrentFile);
            }

            var startedAt = DateTimeOffset.Now;
            var safeRace = SafeFileName.Of(raceName, "race");
            var folder = Path.Combine(_files.DocumentsDirectory, "Sail-Racing videos", safeRace);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{safeRace} start {startedAt:yyyyMMdd-HHmmss}.mp4");

            var wantSound = AppConfig.RecordStartVideoSound;

            // The stamp: caption, GPS line and a subtitle file beside the video, kept up to date while recording.
            var stamp = StampSession.Begin(
                raceName, raceLocation, raceLocationLabel, _geolocation, Path.ChangeExtension(path, ".srt"), _timings.GpsEvery ?? TimeSpan.FromSeconds(10));
            VideoStarted started;
            try
            {
                started = await _recorder.StartAsync(
                    path, AppConfig.PreferredVideoInputDevice.Id, AppConfig.PreferredAudioInputDevice.Id, wantSound, stamp.Overlay);
            }
            catch
            {
                await stamp.DisposeAsync();
                throw;
            }

            IsRecording = true;
            RaceId = raceId;
            CurrentFile = path;
            StartedAt = startedAt;
            HasSound = started.HasSound;
            HasOverlay = started.HasOverlay;
            _stamp = stamp;

            var sound = started.HasSound ? "with sound" : wantSound ? "without sound — no microphone could be used" : "no sound";
            var picture = started.HasOverlay ? "time and GPS on the picture" : "time and GPS in the subtitle file only";
            LastMessage = $"Recording the start ({sound}; {picture})." + (started.Note is null ? string.Empty : " " + started.Note);
            MessageRaceId = raceId;
            Raise();
            return new StartVideoResult(true, LastMessage, path);
        }
        catch (Exception ex)
        {
            LastMessage = $"Could not record the video: {ex.Message}";
            MessageRaceId = raceId;
            Raise();
            return new StartVideoResult(false, LastMessage);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StartVideoResult> StopAsync()
    {
        await CancelScheduledAsync();

        await _gate.WaitAsync();
        try
        {
            if (!IsRecording)
            {
                return new StartVideoResult(true, "No video was being recorded.", LastFile);
            }

            var path = CurrentFile!;
            var raceId = RaceId!;

            try
            {
                await _recorder.StopAsync();
            }
            catch (Exception ex)
            {
                // Say so, but the state below must still be cleared: the recorder is no longer in use.
                await EndStampAsync();
                Finish(path, raceId, $"The video may be incomplete: {ex.Message}");
                return new StartVideoResult(false, LastMessage, path);
            }

            await EndStampAsync();
            Finish(path, raceId, $"Video saved: {path}");
            await RememberAsync(raceId, path);
            return new StartVideoResult(true, LastMessage, path);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EndStampAsync()
    {
        var stamp = _stamp;
        _stamp = null;
        if (stamp is not null)
        {
            await stamp.DisposeAsync();
        }
    }

    private void Finish(string path, string raceId, string message)
    {
        IsRecording = false;
        RaceId = null;
        CurrentFile = null;
        StartedAt = null;
        HasSound = false;
        HasOverlay = false;
        LastFile = path;
        LastRaceId = raceId;
        LastMessage = message;
        MessageRaceId = raceId;
        Raise();
    }

    /// <summary>Keeps the video's location with the race, so it can be opened later from the race's own screen.</summary>
    private async Task RememberAsync(string raceId, string path)
    {
        try
        {
            // Only this one column: screens may be saving the race at this very moment.
            await _races.SetStartVideoPathAsync(raceId, path);
        }
        catch
        {
            // The file is saved either way; only the shortcut to it is lost.
        }
    }
}

/// <summary>
/// What is drawn on the video while it records. Keeps two small text files up to date for the recorder to draw —
/// the caption (race and "START") and the GPS line — and writes a .srt subtitle file beside the video with the time
/// (24-hour) and GPS for every second, so the stamp exists even where the picture itself cannot be drawn on.
/// This PC's own GPS is asked for every few seconds when it has one; until then, or without one, the race's finish
/// line is what the stamp says. Nothing here can stop the recording: every failure just leaves the old text.
/// </summary>
internal sealed class StampSession : IAsyncDisposable
{
    private static readonly TimeSpan FixTimeout = TimeSpan.FromSeconds(3);

    private readonly string _folder;
    private readonly StreamWriter? _srt;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly GeoFix? _raceLocation;
    private readonly string _raceLabel;
    private readonly IGeolocationService _geolocation;
    private readonly TimeSpan _fixEvery;
    private volatile GeoFix? _ownFix;

    public VideoOverlay Overlay { get; }

    private StampSession(
        string folder, VideoOverlay overlay, StreamWriter? srt, GeoFix? raceLocation, string raceLabel, IGeolocationService geolocation, TimeSpan fixEvery)
    {
        _fixEvery = fixEvery;
        _folder = folder;
        Overlay = overlay;
        _srt = srt;
        _raceLocation = raceLocation;
        _raceLabel = raceLabel;
        _geolocation = geolocation;
        _loop = Task.Run(RunAsync);
    }

    public static StampSession Begin(
        string raceName, GeoFix? raceLocation, string raceLabel, IGeolocationService geolocation, string srtPath, TimeSpan fixEvery)
    {
        // A folder of its own, with plain file names: the recorder runs in it and names the files without any path.
        var folder = Path.Combine(Path.GetTempPath(), "sailracing-video-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var titleFile = Path.Combine(folder, "title.txt");
        var gpsFile = Path.Combine(folder, "gps.txt");

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(titleFile, $"{raceName}  —  START", utf8);
        File.WriteAllText(gpsFile, PhotoStamp.GpsLine(raceLocation, raceLabel), utf8);

        StreamWriter? srt = null;
        try
        {
            srt = new StreamWriter(new FileStream(srtPath, FileMode.Create, FileAccess.Write, FileShare.Read), utf8) { AutoFlush = true };
        }
        catch
        {
            // No subtitle file (read-only folder?): the stamp on the picture does not depend on it.
        }

        return new StampSession(folder, new VideoOverlay(titleFile, gpsFile), srt, raceLocation, raceLabel, geolocation, fixEvery);
    }

    private async Task RunAsync()
    {
        var clock = Stopwatch.StartNew();
        TimeSpan? lastFixAsked = null;
        Task? fixTask = null;
        var lastGps = PhotoStamp.GpsLine(_raceLocation, _raceLabel);
        var index = 1;

        try
        {
            while (!_stop.IsCancellationRequested)
            {
                if (_geolocation.IsAvailable && (fixTask is null || fixTask.IsCompleted) && (lastFixAsked is null || clock.Elapsed - lastFixAsked.Value >= _fixEvery))
                {
                    lastFixAsked = clock.Elapsed;
                    fixTask = AskForFixAsync();
                }

                var own = _ownFix;
                var gps = own is not null ? PhotoStamp.GpsLine(own, "this PC") : PhotoStamp.GpsLine(_raceLocation, _raceLabel);
                if (gps != lastGps)
                {
                    WriteWithRetry(Overlay.GpsFile, gps);
                    lastGps = gps;
                }

                if (_srt is not null)
                {
                    var from = clock.Elapsed;
                    _srt.WriteLine(index++);
                    _srt.WriteLine($"{SrtTime(from)} --> {SrtTime(from + TimeSpan.FromSeconds(1))}");
                    _srt.WriteLine($"{PhotoStamp.FormatTimestamp(DateTimeOffset.Now)}   {gps}");
                    _srt.WriteLine();
                }

                await Task.Delay(TimeSpan.FromSeconds(1), _stop.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
        catch
        {
            // A problem with the stamp must never reach the recording.
        }
    }

    private async Task AskForFixAsync()
    {
        try
        {
            var fix = await _geolocation.GetLocationAsync(FixTimeout).WaitAsync(FixTimeout + TimeSpan.FromSeconds(1));
            if (fix is not null)
            {
                _ownFix = fix;
            }
        }
        catch
        {
            // no fix this time; the last one (or the race's) stays
        }
    }

    /// <summary>ffmpeg may be reading the file at this very moment (on Windows that blocks a write), so try a few times.</summary>
    private static void WriteWithRetry(string path, string text)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(20);
            }
        }
    }

    private static string SrtTime(TimeSpan t) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00},{t.Milliseconds:000}");

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try
        {
            await _loop;
        }
        catch
        {
            // already handled in the loop
        }

        _srt?.Dispose();
        _stop.Dispose();

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
            // a leftover temp folder is harmless
        }
    }
}

/// <summary>A name that is safe as a file or folder name on every system we run on.</summary>
internal static class SafeFileName
{
    // Replaced everywhere, not just on Windows, so a folder copied from a Linux PC to a Windows one (or onto a USB
    // stick) still opens: the characters Windows forbids in a name.
    private static readonly char[] Forbidden = { '<', '>', ':', '"', '/', (char)92, '|', '?', '*' }; // 92 is the backslash

    public static string Of(string name, string fallback)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        var cleaned = string.Concat(trimmed.Select(c => char.IsControl(c) || Forbidden.Contains(c) ? '_' : c))
            .TrimEnd('.', ' '); // Windows drops trailing dots and spaces
        return cleaned.Length == 0 ? fallback : cleaned;
    }
}

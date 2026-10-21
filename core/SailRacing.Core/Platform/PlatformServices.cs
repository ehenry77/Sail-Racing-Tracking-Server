namespace SailRacing.Services;

// What differs between the MAUI app and the Avalonia desktop app. The core code (view models, sync,
// start sequence...) only ever talks to these; each app supplies its own implementations.

/// <summary>Moves between screens by route: "raceSetup?raceId=…", "startSequence?raceId=…", "timingSheet?…", "results?…".
/// "//races" goes back to the race list and clears every screen pushed above it.</summary>
public interface INavigator
{
    Task GoToAsync(string route);
}

/// <summary>Questions put to the committee.</summary>
public interface IDialogService
{
    /// <summary>Yes/no question; true when the committee chose <paramref name="accept"/>.</summary>
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);

    /// <summary>Asks for a line of text; null when cancelled.</summary>
    Task<string?> PromptAsync(string title, string message, string accept, string cancel, string? placeholder = null);
}

public interface IClipboardService
{
    Task SetTextAsync(string text);
}

/// <summary>Opens a link in the default browser.</summary>
public interface ILauncherService
{
    Task OpenAsync(Uri uri);

    /// <summary>Opens a file (e.g. a photo) in the program the system associates with it.</summary>
    Task OpenFileAsync(string path);
}

/// <summary>Files: where exports go, handing a file to the user, and choosing a file to import.</summary>
public interface IFileService
{
    /// <summary>The user's documents folder.</summary>
    string DocumentsDirectory { get; }

    /// <summary>A scratch folder for files that only exist to be shared.</summary>
    string CacheDirectory { get; }

    /// <summary>
    /// Hands a saved file to the user — the system share sheet where there is one, otherwise showing it in the
    /// file manager. Never throws: the file is already saved where the caller says.
    /// </summary>
    Task ShareFileAsync(string title, string path);

    /// <summary>Lets the user pick a text file (e.g. ".json") and returns its name and contents; null when cancelled.</summary>
    Task<PickedTextFile?> PickTextFileAsync(string title, params string[] extensions);
}

public record PickedTextFile(string Name, string Text);

public record GeoFix(double Latitude, double Longitude);

/// <summary>The device's position, for capturing buoys and the start line. Platforms without a location
/// source report <see cref="IsAvailable"/> false and the coordinates are typed in instead.</summary>
public interface IGeolocationService
{
    bool IsAvailable { get; }

    /// <summary>Best available fix, or null if none could be obtained within the timeout. Throws with a readable
    /// message when location is switched off or not permitted.</summary>
    Task<GeoFix?> GetLocationAsync(TimeSpan timeout);
}

/// <summary>Key/value store for the committee's settings (server address, start-sequence parameters, device choices).</summary>
public interface ISettingsStore
{
    string? Get(string key);

    /// <summary>Stores the value; null removes the key.</summary>
    void Set(string key, string? value);
}

/// <summary>Where the app keeps its local database.</summary>
public interface IAppPaths
{
    string DataDirectory { get; }
}

/// <summary>Reports when the machine gains or loses its network connection, so queued work can be retried.</summary>
public interface IConnectivityService
{
    /// <summary>Raised with true when internet access is (back) available.</summary>
    event EventHandler<bool>? ConnectivityChanged;
}

/// <summary>A repeating timer whose ticks arrive on the UI thread.</summary>
public interface IUiTimer
{
    TimeSpan Interval { get; set; }

    event EventHandler? Tick;

    void Start();

    void Stop();
}

/// <summary>Runs work on the UI thread and creates timers that tick there.</summary>
public interface IUiDispatcher
{
    void Post(Action action);

    IUiTimer CreateTimer();
}

/// <summary>The committee PC's camera (a webcam on a desktop), for the photo at a boat's finish.</summary>
public interface ICameraService
{
    /// <summary>Opens the camera ahead of time so a later capture is immediate. Never throws.</summary>
    Task WarmUpAsync(string? deviceId);

    /// <summary>A JPEG still from the camera (the one chosen in Settings, or the default). Throws
    /// <see cref="InvalidOperationException"/> with a readable reason when there is no camera or it may not be used.</summary>
    Task<byte[]> CaptureJpegAsync(string? deviceId);

    /// <summary>Closes the camera. Never throws.</summary>
    Task ReleaseAsync();
}

/// <summary>
/// The text drawn on the picture of a video: the caption and the GPS line are read from these files on every frame, so
/// the GPS line can change while recording; the clock is drawn from the machine's own clock. Both files are in one folder.
/// </summary>
public sealed record VideoOverlay(string TitleFile, string GpsFile);

/// <param name="HasSound">The microphone's sound is being recorded.</param>
/// <param name="HasOverlay">The time and GPS are drawn on the picture (not just kept in the subtitle file).</param>
/// <param name="Note">Something worth telling the committee, e.g. why a fall-back recorder was used.</param>
public sealed record VideoStarted(bool HasSound, bool HasOverlay, string? Note = null);

/// <summary>Records video (and, if wanted, sound) to a file — the video of a start, from the last minute to All Clear.</summary>
public interface IVideoRecorder
{
    bool IsRecording { get; }

    /// <summary>Switches the camera (and microphone) on ahead of time, so <see cref="StartAsync"/> begins at once. Never throws.</summary>
    Task PrepareAsync(string? videoDeviceId, string? audioDeviceId, bool withSound);

    /// <summary>
    /// Starts recording to <paramref name="filePath"/> with the camera (and microphone) chosen in Settings, or the
    /// defaults, drawing <paramref name="overlay"/> on the picture if the recorder can. Returns once recording has begun,
    /// saying whether sound and the overlay are really there. Throws <see cref="InvalidOperationException"/> with a
    /// readable reason when recording cannot start.
    /// </summary>
    Task<VideoStarted> StartAsync(string filePath, string? videoDeviceId, string? audioDeviceId, bool withSound, VideoOverlay? overlay);

    /// <summary>Stops and finalises the file. Throws <see cref="InvalidOperationException"/> if it may be incomplete.</summary>
    Task StopAsync();

    /// <summary>Switches off a camera prepared but not used. Never throws; does nothing while recording.</summary>
    Task ReleaseAsync();
}

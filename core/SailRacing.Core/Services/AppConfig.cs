namespace SailRacing.Services;

/// <summary>
/// Runtime-configurable settings, persisted via MAUI Preferences so the committee can change them
/// without a rebuild. Edited from the Settings tab (Views/Settings/SettingsPage).
/// </summary>
public static class AppConfig
{
    /// <summary>
    /// Where settings live. Each app installs its own at start-up (MAUI Preferences, or a JSON file on the
    /// desktop); until then they are kept in memory, which is also what tests get.
    /// </summary>
    public static ISettingsStore Store { get; set; } = new InMemorySettingsStore();

    private const string ServerBaseUrlKey = "ServerBaseUrl";
    private const string DefaultServerBaseUrl = "https://sail-racing.example.com";

    private const string AudioOutputDeviceIdKey = "AudioOutputDeviceId";
    private const string AudioOutputDeviceNameKey = "AudioOutputDeviceName";
    private const string AudioInputDeviceIdKey = "AudioInputDeviceId";
    private const string AudioInputDeviceNameKey = "AudioInputDeviceName";
    private const string VideoInputDeviceIdKey = "VideoInputDeviceId";
    private const string VideoInputDeviceNameKey = "VideoInputDeviceName";
    private const string GpsSensorDeviceIdKey = "GpsSensorDeviceId";
    private const string GpsSensorDeviceNameKey = "GpsSensorDeviceName";

    private const string AutoFinishPhotoKey = "AutoFinishPhoto";

    /// <summary>Take a photo with the camera automatically when a boat finishes (off until switched on in Settings).</summary>
    public static bool AutoFinishPhoto
    {
        get => Store.Get(AutoFinishPhotoKey) == "1";
        set => Store.Set(AutoFinishPhotoKey, value ? "1" : "0");
    }

    private const string RecordStartVideoKey = "RecordStartVideo";
    private const string RecordStartVideoSoundKey = "RecordStartVideoSound";

    /// <summary>Record a video of each start, from Begin Start Sequence until All Clear (off until switched on in Settings).</summary>
    public static bool RecordStartVideo
    {
        get => Store.Get(RecordStartVideoKey) == "1";
        set => Store.Set(RecordStartVideoKey, value ? "1" : "0");
    }

    /// <summary>Include the microphone's sound in the start video (on unless switched off).</summary>
    public static bool RecordStartVideoSound
    {
        get => Store.Get(RecordStartVideoSoundKey) != "0";
        set => Store.Set(RecordStartVideoSoundKey, value ? "1" : "0");
    }

    public static string ServerBaseUrl
    {
        get => (Store.Get(ServerBaseUrlKey) ?? DefaultServerBaseUrl);
        set => Store.Set(ServerBaseUrlKey, value);
    }

    public static (string? Id, string? Name) PreferredAudioOutputDevice
    {
        get => (Store.Get(AudioOutputDeviceIdKey), Store.Get(AudioOutputDeviceNameKey));
        set
        {
            Store.Set(AudioOutputDeviceIdKey, value.Id);
            Store.Set(AudioOutputDeviceNameKey, value.Name);
        }
    }

    public static (string? Id, string? Name) PreferredAudioInputDevice
    {
        get => (Store.Get(AudioInputDeviceIdKey), Store.Get(AudioInputDeviceNameKey));
        set
        {
            Store.Set(AudioInputDeviceIdKey, value.Id);
            Store.Set(AudioInputDeviceNameKey, value.Name);
        }
    }

    public static (string? Id, string? Name) PreferredVideoInputDevice
    {
        get => (Store.Get(VideoInputDeviceIdKey), Store.Get(VideoInputDeviceNameKey));
        set
        {
            Store.Set(VideoInputDeviceIdKey, value.Id);
            Store.Set(VideoInputDeviceNameKey, value.Name);
        }
    }

    // Start sequence parameters, stored as the text the committee typed so Settings shows it back exactly.
    private const string SequenceAlarmsKey = "SequenceAlarms";
    private const string SequenceCountdownKey = "SequenceCountdownSeconds";
    private const string SequenceSignalsKey = "SequenceSignals";

    public static string SequenceAlarmsText
    {
        get => (Store.Get(SequenceAlarmsKey) ?? StartSequenceSettings.DefaultAlarmsText);
        set => Store.Set(SequenceAlarmsKey, value);
    }

    public static string SequenceCountdownText
    {
        get => (Store.Get(SequenceCountdownKey) ?? StartSequenceSettings.DefaultCountdownText);
        set => Store.Set(SequenceCountdownKey, value);
    }

    public static string SequenceSignalsText
    {
        get => (Store.Get(SequenceSignalsKey) ?? StartSequenceSettings.DefaultSignalsText);
        set => Store.Set(SequenceSignalsKey, value);
    }

    /// <summary>The sequence to run now. Stored values are validated when saved, but if they ever don't parse
    /// (edited by hand, or from an older version) the standard 5-4-1-0 sequence is used rather than failing at the start.</summary>
    public static StartSequenceSettings LoadSequenceSettings() =>
        StartSequenceSettings.TryParse(SequenceAlarmsText, SequenceCountdownText, SequenceSignalsText, out var settings, out _)
            ? settings
            : StartSequenceSettings.Default;

    public static (string? Id, string? Name) PreferredGpsSensor
    {
        get => (Store.Get(GpsSensorDeviceIdKey), Store.Get(GpsSensorDeviceNameKey));
        set
        {
            Store.Set(GpsSensorDeviceIdKey, value.Id);
            Store.Set(GpsSensorDeviceNameKey, value.Name);
        }
    }
}

namespace SailRacing.Services;

/// <summary>
/// Runtime-configurable settings, persisted via MAUI Preferences so the committee can change them
/// without a rebuild. Edited from the Settings tab (Views/Settings/SettingsPage).
/// </summary>
public static class AppConfig
{
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

    public static string ServerBaseUrl
    {
        get => Preferences.Default.Get(ServerBaseUrlKey, DefaultServerBaseUrl);
        set => Preferences.Default.Set(ServerBaseUrlKey, value);
    }

    public static (string? Id, string? Name) PreferredAudioOutputDevice
    {
        get => (Preferences.Default.Get<string?>(AudioOutputDeviceIdKey, null), Preferences.Default.Get<string?>(AudioOutputDeviceNameKey, null));
        set
        {
            Preferences.Default.Set(AudioOutputDeviceIdKey, value.Id);
            Preferences.Default.Set(AudioOutputDeviceNameKey, value.Name);
        }
    }

    public static (string? Id, string? Name) PreferredAudioInputDevice
    {
        get => (Preferences.Default.Get<string?>(AudioInputDeviceIdKey, null), Preferences.Default.Get<string?>(AudioInputDeviceNameKey, null));
        set
        {
            Preferences.Default.Set(AudioInputDeviceIdKey, value.Id);
            Preferences.Default.Set(AudioInputDeviceNameKey, value.Name);
        }
    }

    public static (string? Id, string? Name) PreferredVideoInputDevice
    {
        get => (Preferences.Default.Get<string?>(VideoInputDeviceIdKey, null), Preferences.Default.Get<string?>(VideoInputDeviceNameKey, null));
        set
        {
            Preferences.Default.Set(VideoInputDeviceIdKey, value.Id);
            Preferences.Default.Set(VideoInputDeviceNameKey, value.Name);
        }
    }

    // Start sequence parameters, stored as the text the committee typed so Settings shows it back exactly.
    private const string SequenceAlarmsKey = "SequenceAlarms";
    private const string SequenceCountdownKey = "SequenceCountdownSeconds";
    private const string SequenceSignalsKey = "SequenceSignals";

    public static string SequenceAlarmsText
    {
        get => Preferences.Default.Get(SequenceAlarmsKey, StartSequenceSettings.DefaultAlarmsText);
        set => Preferences.Default.Set(SequenceAlarmsKey, value);
    }

    public static string SequenceCountdownText
    {
        get => Preferences.Default.Get(SequenceCountdownKey, StartSequenceSettings.DefaultCountdownText);
        set => Preferences.Default.Set(SequenceCountdownKey, value);
    }

    public static string SequenceSignalsText
    {
        get => Preferences.Default.Get(SequenceSignalsKey, StartSequenceSettings.DefaultSignalsText);
        set => Preferences.Default.Set(SequenceSignalsKey, value);
    }

    /// <summary>The sequence to run now. Stored values are validated when saved, but if they ever don't parse
    /// (edited by hand, or from an older version) the standard 5-4-1-0 sequence is used rather than failing at the start.</summary>
    public static StartSequenceSettings LoadSequenceSettings() =>
        StartSequenceSettings.TryParse(SequenceAlarmsText, SequenceCountdownText, SequenceSignalsText, out var settings, out _)
            ? settings
            : StartSequenceSettings.Default;

    public static (string? Id, string? Name) PreferredGpsSensor
    {
        get => (Preferences.Default.Get<string?>(GpsSensorDeviceIdKey, null), Preferences.Default.Get<string?>(GpsSensorDeviceNameKey, null));
        set
        {
            Preferences.Default.Set(GpsSensorDeviceIdKey, value.Id);
            Preferences.Default.Set(GpsSensorDeviceNameKey, value.Name);
        }
    }
}

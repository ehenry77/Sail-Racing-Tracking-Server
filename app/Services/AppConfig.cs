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
}

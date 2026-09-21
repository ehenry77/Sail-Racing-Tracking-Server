namespace SailRacing.Services;

/// <summary>
/// Runtime-configurable settings, persisted via MAUI Preferences so the committee can point the app
/// at the deployed Node server without a rebuild. TODO: wire ServerBaseUrl to a Settings page UI.
/// </summary>
public static class AppConfig
{
    private const string ServerBaseUrlKey = "ServerBaseUrl";
    private const string DefaultServerBaseUrl = "https://sail-racing.example.com";

    public static string ServerBaseUrl
    {
        get => Preferences.Default.Get(ServerBaseUrlKey, DefaultServerBaseUrl);
        set => Preferences.Default.Set(ServerBaseUrlKey, value);
    }
}

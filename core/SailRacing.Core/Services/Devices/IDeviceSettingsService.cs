namespace SailRacing.Services.Devices;

/// <summary>
/// Enumerates audio/video I/O devices. Implemented per-platform (Platforms/&lt;Platform&gt;/DeviceSettingsService.cs)
/// since .NET MAUI has no cross-platform API for this — each platform's implementation calls its native
/// device-enumeration APIs directly. Nothing in the app currently captures audio or video; this only
/// lists devices and remembers the committee's preferred one (see AppConfig) for a future feature to use.
/// </summary>
public interface IDeviceSettingsService
{
    Task<List<MediaDeviceInfo>> GetAudioOutputDevicesAsync();

    Task<List<MediaDeviceInfo>> GetAudioInputDevicesAsync();

    Task<List<MediaDeviceInfo>> GetVideoInputDevicesAsync();

    /// <summary>
    /// Location/GPS sensors registered with the OS (e.g. a laptop's internal sensor plus any external
    /// GPS receiver that installs as a Windows location sensor). Real enumeration is only implemented
    /// on Windows, where multiple such sensors are actually common; other platforms return an empty list
    /// since mobile devices only ever expose one GPS chip, which MAUI's Geolocation API already targets.
    /// </summary>
    Task<List<MediaDeviceInfo>> GetGpsSensorsAsync();
}

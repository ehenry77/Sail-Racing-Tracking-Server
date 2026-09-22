using Windows.Devices.Enumeration;
using SailRacing.Services.Devices;

namespace SailRacing.Services.Devices;

public class DeviceSettingsService : IDeviceSettingsService
{
    public async Task<List<MediaDeviceInfo>> GetAudioOutputDevicesAsync() =>
        await EnumerateAsync(DeviceClass.AudioRender);

    public async Task<List<MediaDeviceInfo>> GetAudioInputDevicesAsync() =>
        await EnumerateAsync(DeviceClass.AudioCapture);

    public async Task<List<MediaDeviceInfo>> GetVideoInputDevicesAsync() =>
        await EnumerateAsync(DeviceClass.VideoCapture);

    private static async Task<List<MediaDeviceInfo>> EnumerateAsync(DeviceClass deviceClass)
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(deviceClass);
            return devices.Select(d => new MediaDeviceInfo(d.Id, d.Name)).ToList();
        }
        catch
        {
            // No permission granted yet, or no devices — present an empty list rather than crashing the page.
            return new List<MediaDeviceInfo>();
        }
    }
}

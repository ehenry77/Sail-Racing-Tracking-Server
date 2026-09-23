using AVFoundation;
using SailRacing.Services.Devices;

namespace SailRacing.Services.Devices;

public class DeviceSettingsService : IDeviceSettingsService
{
    public Task<List<MediaDeviceInfo>> GetAudioOutputDevicesAsync()
    {
        var result = new List<MediaDeviceInfo>();
        try
        {
            // AVAudioSession has no general "list all possible outputs" API — only the currently
            // active route's outputs (e.g. Speaker, Headphones, a connected Bluetooth device).
            var outputs = AVAudioSession.SharedInstance().CurrentRoute?.Outputs;
            if (outputs is not null)
            {
                foreach (var output in outputs)
                {
                    result.Add(new MediaDeviceInfo(output.UID, output.PortName));
                }
            }
        }
        catch
        {
            // Best-effort.
        }

        return Task.FromResult(result);
    }

    public Task<List<MediaDeviceInfo>> GetAudioInputDevicesAsync()
    {
        var result = new List<MediaDeviceInfo>();
        try
        {
            var inputs = AVAudioSession.SharedInstance().AvailableInputs;
            if (inputs is not null)
            {
                foreach (var input in inputs)
                {
                    result.Add(new MediaDeviceInfo(input.UID, input.PortName));
                }
            }
        }
        catch
        {
            // Best-effort — mic permission not yet granted, or no inputs.
        }

        return Task.FromResult(result);
    }

    public Task<List<MediaDeviceInfo>> GetGpsSensorsAsync() =>
        // Mac Catalyst exposes at most one location source to apps, which MAUI's Geolocation API
        // already targets — there's no OS concept of multiple selectable location sensors like on Windows.
        Task.FromResult(new List<MediaDeviceInfo>());

    public Task<List<MediaDeviceInfo>> GetVideoInputDevicesAsync()
    {
        var result = new List<MediaDeviceInfo>();
        try
        {
            foreach (var device in AVCaptureDevice.DevicesWithMediaType(AVMediaType.Video))
            {
                result.Add(new MediaDeviceInfo(device.UniqueID, device.LocalizedName));
            }
        }
        catch
        {
            // Best-effort — camera permission not yet granted, or no cameras.
        }

        return Task.FromResult(result);
    }
}

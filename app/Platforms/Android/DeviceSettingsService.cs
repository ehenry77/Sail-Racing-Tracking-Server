using Android.Content;
using Android.Hardware.Camera2;
using Android.Media;
using SailRacing.Services.Devices;

namespace SailRacing.Services.Devices;

public class DeviceSettingsService : IDeviceSettingsService
{
    public Task<List<MediaDeviceInfo>> GetAudioOutputDevicesAsync() =>
        Task.FromResult(EnumerateAudio(GetDevicesTargets.Outputs));

    public Task<List<MediaDeviceInfo>> GetAudioInputDevicesAsync() =>
        Task.FromResult(EnumerateAudio(GetDevicesTargets.Inputs));

    public Task<List<MediaDeviceInfo>> GetGpsSensorsAsync() =>
        // Android exposes exactly one GPS chip to apps, which MAUI's Geolocation API already targets —
        // there's no OS concept of multiple selectable location sensors like on Windows.
        Task.FromResult(new List<MediaDeviceInfo>());

    public Task<List<MediaDeviceInfo>> GetVideoInputDevicesAsync()
    {
        var result = new List<MediaDeviceInfo>();
        try
        {
            var context = Android.App.Application.Context;
            if (context.GetSystemService(Context.CameraService) is CameraManager cameraManager)
            {
                foreach (var id in cameraManager.GetCameraIdList() ?? Array.Empty<string>())
                {
                    var characteristics = cameraManager.GetCameraCharacteristics(id);
                    var facing = (int?)(Java.Lang.Integer?)characteristics.Get(CameraCharacteristics.LensFacing);
                    var label = facing switch
                    {
                        (int)LensFacing.Front => $"Front camera ({id})",
                        (int)LensFacing.Back => $"Back camera ({id})",
                        _ => $"Camera {id}"
                    };
                    result.Add(new MediaDeviceInfo(id, label));
                }
            }
        }
        catch
        {
            // No camera permission yet, or no cameras — present an empty list rather than crashing the page.
        }

        return Task.FromResult(result);
    }

    private static List<MediaDeviceInfo> EnumerateAudio(GetDevicesTargets targets)
    {
        var result = new List<MediaDeviceInfo>();

        // AudioManager.GetDevices requires API 23+; SupportedOSPlatformVersion for this app is 21.
        if (!OperatingSystem.IsAndroidVersionAtLeast(23))
        {
            return result;
        }

        try
        {
            var context = Android.App.Application.Context;
            if (context.GetSystemService(Context.AudioService) is AudioManager audioManager)
            {
                foreach (var device in audioManager.GetDevices(targets))
                {
                    var name = device.ProductName?.ToString() ?? device.Type.ToString();
                    result.Add(new MediaDeviceInfo(device.Id.ToString(), name));
                }
            }
        }
        catch
        {
            // Best-effort — missing permission or no devices.
        }

        return result;
    }
}

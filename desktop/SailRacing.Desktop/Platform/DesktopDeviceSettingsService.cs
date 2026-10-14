using System.Diagnostics;
using SailRacing.Services.Devices;

namespace SailRacing.Desktop.Platform;

/// <summary>
/// Lists audio devices on Linux through PulseAudio/PipeWire's <c>pactl</c> and cameras from <c>/dev/video*</c>.
/// Nothing in the app captures audio or video yet, so this only lets the committee note a preference (as on
/// the other platforms). Where the tool isn't there — or on another OS — the lists are simply empty.
/// There is no GPS sensor list: Linux has no standard location service.
/// </summary>
public sealed class DesktopDeviceSettingsService : IDeviceSettingsService
{
    public Task<List<MediaDeviceInfo>> GetAudioOutputDevicesAsync() => PactlAsync("sinks");

    public Task<List<MediaDeviceInfo>> GetAudioInputDevicesAsync() => PactlAsync("sources", skipMonitors: true);

    public Task<List<MediaDeviceInfo>> GetVideoInputDevicesAsync()
    {
        var devices = new List<MediaDeviceInfo>();
        if (OperatingSystem.IsLinux() && Directory.Exists("/dev"))
        {
            foreach (var path in Directory.GetFiles("/dev", "video*").OrderBy(p => p, StringComparer.Ordinal))
            {
                devices.Add(new MediaDeviceInfo(path, Path.GetFileName(path)));
            }
        }

        return Task.FromResult(devices);
    }

    public Task<List<MediaDeviceInfo>> GetGpsSensorsAsync() => Task.FromResult(new List<MediaDeviceInfo>());

    private static async Task<List<MediaDeviceInfo>> PactlAsync(string kind, bool skipMonitors = false)
    {
        var devices = new List<MediaDeviceInfo>();
        if (!OperatingSystem.IsLinux())
        {
            return devices;
        }

        try
        {
            var info = new ProcessStartInfo("pactl")
            {
                ArgumentList = { "list", "short", kind },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(info)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                // "<index>\t<name>\t<driver>\t<format>\t<state>"
                var columns = line.Split('\t');
                if (columns.Length < 2 || (skipMonitors && columns[1].EndsWith(".monitor", StringComparison.Ordinal)))
                {
                    continue;
                }

                devices.Add(new MediaDeviceInfo(columns[1], columns[1]));
            }
        }
        catch
        {
            // pactl not installed: no list.
        }

        return devices;
    }
}

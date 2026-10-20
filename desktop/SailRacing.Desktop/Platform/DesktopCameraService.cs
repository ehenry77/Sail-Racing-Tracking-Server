using System.Diagnostics;
using SailRacing.Services;

namespace SailRacing.Desktop.Platform;

/// <summary>
/// The webcam on Linux, read with <c>ffmpeg</c> (or, failing that, <c>fswebcam</c>) from a Video4Linux device such as
/// <c>/dev/video0</c> — install with <c>sudo apt install ffmpeg</c>. Each photo runs the tool once and skips the first
/// few frames so the webcam has settled its exposure, which takes about a second; there is nothing to keep open, so
/// the camera light is only on while a photo is being taken. Other systems are not supported by this app: on Windows
/// use the Windows version, which has a proper webcam interface.
/// </summary>
public sealed class DesktopCameraService : ICameraService
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(20);

    public Task WarmUpAsync(string? deviceId) => Task.CompletedTask;

    public Task ReleaseAsync() => Task.CompletedTask;

    public async Task<byte[]> CaptureJpegAsync(string? deviceId)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new InvalidOperationException(
                "this version takes photos on Linux only — on Windows use the Windows version of SailRacing");
        }

        var device = string.IsNullOrWhiteSpace(deviceId) ? FirstVideoDevice() : deviceId;
        if (device is null)
        {
            throw new InvalidOperationException("no camera was found (there is no /dev/video0) — is the webcam plugged in?");
        }

        var file = Path.Combine(Path.GetTempPath(), $"sailracing-photo-{Guid.NewGuid():N}.jpg");
        try
        {
            if (OnPath("ffmpeg"))
            {
                // select=gte(n,4) drops the first four frames, which are dark while the webcam adjusts.
                await RunAsync("ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-f", "v4l2", "-i", device,
                    "-vf", "select=gte(n\\,4)", "-vsync", "vfr", "-frames:v", "1", "-q:v", "2", file);
            }
            else if (OnPath("fswebcam"))
            {
                await RunAsync("fswebcam", "-d", device, "-r", "1280x720", "--no-banner", "-S", "5", "--jpeg", "90", file);
            }
            else
            {
                throw new InvalidOperationException("reading the camera needs ffmpeg (or fswebcam) — run: sudo apt install ffmpeg");
            }

            if (!File.Exists(file) || new FileInfo(file).Length == 0)
            {
                throw new InvalidOperationException($"the camera ({device}) gave no picture");
            }

            return await File.ReadAllBytesAsync(file);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // a leftover temp file is harmless
            }
        }
    }

    private static string? FirstVideoDevice() =>
        Directory.Exists("/dev")
            ? Directory.GetFiles("/dev", "video*").OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault()
            : null;

    private static async Task RunAsync(string tool, params string[] args)
    {
        var info = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"{tool} could not be started");
        var errors = process.StandardError.ReadToEndAsync();
        _ = process.StandardOutput.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(CaptureTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw new InvalidOperationException($"{tool} did not finish in time — is another program using the camera?");
        }

        if (process.ExitCode != 0)
        {
            var message = (await errors).Trim();
            throw new InvalidOperationException($"{tool} failed ({(message.Length > 0 ? message.Split('\n')[^1] : "exit code " + process.ExitCode)})");
        }
    }

    private static bool OnPath(string tool)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, tool)));
    }
}

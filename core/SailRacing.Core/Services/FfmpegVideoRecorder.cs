using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SailRacing.Services;

/// <summary>How the camera and microphone are named to ffmpeg on this system, and where ffmpeg and a font are.</summary>
public interface IFfmpegCapture
{
    /// <summary>Full path of ffmpeg, or null if it is not installed.</summary>
    string? FindFfmpeg();

    /// <summary>A bold font file for the text drawn on the video (null: ffmpeg's own default, which needs fontconfig).</summary>
    string? FontFile { get; }

    /// <summary>
    /// The input arguments that read the camera (and, with <paramref name="withAudio"/>, the microphone). Throws
    /// <see cref="InvalidOperationException"/> with a readable reason when there is no camera or no way to read it.
    /// </summary>
    Task<IReadOnlyList<string>> InputArgumentsAsync(string ffmpeg, string? videoDeviceId, string? audioDeviceId, bool withAudio);
}

/// <summary>
/// Records the video of a start by running <c>ffmpeg</c> on the camera and microphone, drawing the stamp on the picture
/// as it goes: a caption (race and "START"), the date and time to the second in 24-hour format, and the GPS position.
/// The file is a fragmented MP4, so it plays even if the program is stopped abruptly; stopping normally asks ffmpeg to
/// finish ("q"), which writes it out properly. If the sound cannot be opened ffmpeg is started again without it, so
/// the picture is never lost for want of a microphone. As a safeguard against a recording left running by a crash,
/// ffmpeg is told to stop by itself after an hour.
/// </summary>
public sealed class FfmpegVideoRecorder : IVideoRecorder
{
    private static readonly TimeSpan StartupCheck = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);

    private readonly IFfmpegCapture _capture;
    private Process? _process;
    private Task<string>? _errors;
    private string? _file;

    public FfmpegVideoRecorder(IFfmpegCapture capture)
    {
        _capture = capture;
    }

    public bool IsRecording => _process is { HasExited: false };

    /// <summary>True when ffmpeg is installed, i.e. the video can be recorded and stamped this way.</summary>
    public bool IsAvailable => _capture.FindFfmpeg() is not null;

    // ffmpeg starts quickly; there is no camera to switch on ahead of time.
    public Task PrepareAsync(string? videoDeviceId, string? audioDeviceId, bool withSound) => Task.CompletedTask;

    public Task ReleaseAsync() => Task.CompletedTask;

    public async Task<VideoStarted> StartAsync(string filePath, string? videoDeviceId, string? audioDeviceId, bool withSound, VideoOverlay? overlay)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("a video is already being recorded");
        }

        var ffmpeg = _capture.FindFfmpeg()
            ?? throw new InvalidOperationException("recording video needs ffmpeg (Windows: winget install Gyan.FFmpeg — Linux: sudo apt install ffmpeg)");

        // Some builds of ffmpeg (certain static ones) have no text-drawing filter. Record without the stamp rather than not at all.
        string? note = null;
        if (overlay is not null && !await CanDrawTextAsync(ffmpeg))
        {
            overlay = null;
            note = "This copy of ffmpeg cannot draw text on video (no drawtext filter — the Ubuntu/Debian package and the Windows \"essentials\" build have it), so the time and GPS are in the subtitle file only.";
        }

        if (withSound)
        {
            var failure = await TryStartAsync(ffmpeg, filePath, videoDeviceId, audioDeviceId, withAudio: true, overlay);
            if (failure is null)
            {
                return new VideoStarted(HasSound: true, HasOverlay: overlay is not null, note);
            }

            // No usable microphone (or no sound system): record the picture anyway.
            var videoOnlyFailure = await TryStartAsync(ffmpeg, filePath, videoDeviceId, audioDeviceId, withAudio: false, overlay);
            return videoOnlyFailure is null
                ? new VideoStarted(HasSound: false, HasOverlay: overlay is not null, note)
                : throw new InvalidOperationException(videoOnlyFailure);
        }

        var error = await TryStartAsync(ffmpeg, filePath, videoDeviceId, audioDeviceId, withAudio: false, overlay);
        return error is null
            ? new VideoStarted(HasSound: false, HasOverlay: overlay is not null, note)
            : throw new InvalidOperationException(error);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> DrawTextSupport = new();

    /// <summary>Whether this ffmpeg has the filters the stamp needs (asked once per ffmpeg, then remembered).</summary>
    internal static async Task<bool> CanDrawTextAsync(string ffmpeg)
    {
        if (DrawTextSupport.TryGetValue(ffmpeg, out var known))
        {
            return known;
        }

        var filters = await FfmpegCapture.RunForOutputAsync(ffmpeg, "-hide_banner", "-filters");
        var supported = Regex.IsMatch(filters, @"\sdrawtext\s") && Regex.IsMatch(filters, @"\sdrawbox\s");
        DrawTextSupport[ffmpeg] = supported;
        return supported;
    }

    /// <summary>The ffmpeg arguments for one recording (separate from starting it, so they can be inspected).</summary>
    public async Task<List<string>> BuildArgumentsAsync(
        string ffmpeg, string filePath, string? videoDeviceId, string? audioDeviceId, bool withAudio, VideoOverlay? overlay)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
        args.AddRange(await _capture.InputArgumentsAsync(ffmpeg, videoDeviceId, audioDeviceId, withAudio));

        if (overlay is not null)
        {
            args.Add("-vf");
            args.Add(OverlayFilter(overlay, _capture.FontFile));
        }

        args.AddRange(new[] { "-c:v", "libx264", "-preset", "veryfast", "-crf", "26", "-pix_fmt", "yuv420p" });
        if (withAudio)
        {
            args.AddRange(new[] { "-c:a", "aac", "-b:a", "128k" });
        }

        args.AddRange(new[] { "-t", "3600", "-movflags", "+frag_keyframe+empty_moov+default_base_moof", filePath });
        return args;
    }

    /// <summary>
    /// The picture filter that draws the stamp along the bottom: a dark band, the caption, the live clock and the GPS
    /// line. The caption and GPS line come from small text files that ffmpeg re-reads on every frame (so the GPS line can
    /// change while recording); the clock is ffmpeg's own wall clock, so it is exact. ffmpeg runs in the files' folder,
    /// so they are named plainly and no path needs escaping.
    /// </summary>
    public static string OverlayFilter(VideoOverlay overlay, string? fontFile)
    {
        var font = fontFile is null ? string.Empty : $"fontfile={EscapeFilterValue(fontFile)}:";
        string Text(string textFile, string y) =>
            $"drawtext={font}fontcolor=white:fontsize=h/30:x=h/60:y={y}:textfile={Path.GetFileName(textFile)}:reload=1";

        // %{localtime:FORMAT} is expanded afresh for every frame. Each field is expanded on its own, with the colons written
        // as literals, so the escaping stays simple. The UTC offset is fixed for the recording, so it is plain text.
        var offset = DateTimeOffset.Now.Offset;
        var offsetText = (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        var clock =
            @"%{localtime\:%Y-%m-%d} %{localtime\:%H}\:%{localtime\:%M}\:%{localtime\:%S} (UTC" + offsetText.Replace(":", @"\:") + ")";

        return string.Join(",",
            "drawbox=x=0:y=ih-ih*0.15:w=iw:h=ih*0.15:color=black@0.6:t=fill",
            Text(overlay.TitleFile, "h*0.855"),
            $"drawtext={font}fontcolor=white:fontsize=h/30:x=h/60:y=h*0.9:text='{clock}'",
            Text(overlay.GpsFile, "h*0.945"));
    }

    /// <summary>
    /// A value (a path) written for use inside an ffmpeg filter: slashes forward, always in single quotes. Quoting is
    /// needed even when the path has no spaces — an unquoted backslash is consumed at the filter's first parsing level, so
    /// "C\:" would still split the option at the colon. Inside the quotes only the colon needs its backslash, and a quote
    /// in the path is closed, escaped and reopened.
    /// </summary>
    internal static string EscapeFilterValue(string value)
    {
        var escaped = value.Replace('\\', '/').Replace(":", @"\:").Replace("'", @"'\''");
        return $"'{escaped}'";
    }

    /// <summary>Starts ffmpeg and waits a moment to see that it stays up. Returns null on success, otherwise why it did not.</summary>
    private async Task<string?> TryStartAsync(
        string ffmpeg, string filePath, string? videoDeviceId, string? audioDeviceId, bool withAudio, VideoOverlay? overlay)
    {
        List<string> args;
        try
        {
            args = await BuildArgumentsAsync(ffmpeg, filePath, videoDeviceId, audioDeviceId, withAudio, overlay);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        var info = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        // The overlay's text files are found by name in the working folder.
        if (overlay is not null)
        {
            info.WorkingDirectory = Path.GetDirectoryName(overlay.TitleFile)!;
        }

        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException("ffmpeg could not be started");
        }
        catch (Exception ex)
        {
            return $"ffmpeg could not be started ({ex.Message})";
        }

        var errors = process.StandardError.ReadToEndAsync();
        _ = process.StandardOutput.ReadToEndAsync();

        await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(StartupCheck));
        if (process.HasExited)
        {
            var message = (await errors).Trim();
            var last = message.Length > 0 ? message.Split('\n')[^1].Trim() : $"exit code {process.ExitCode}";
            process.Dispose();
            return $"ffmpeg stopped at once ({last})";
        }

        _process = process;
        _errors = errors;
        _file = filePath;
        return null;
    }

    public async Task StopAsync()
    {
        var process = _process;
        var errors = _errors;
        var file = _file;
        _process = null;
        _errors = null;
        _file = null;

        if (process is null)
        {
            return;
        }

        using (process)
        {
            try
            {
                if (!process.HasExited)
                {
                    // "q" is how ffmpeg is asked to stop and finish the file properly.
                    await process.StandardInput.WriteLineAsync("q");
                    await process.StandardInput.FlushAsync();

                    using var timeout = new CancellationTokenSource(StopTimeout);
                    try
                    {
                        await process.WaitForExitAsync(timeout.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                        throw new InvalidOperationException("ffmpeg did not finish in time, so the file may be incomplete");
                    }
                }
            }
            catch (IOException)
            {
                // ffmpeg had already ended (its input is closed); the checks below say whether the file is usable.
            }

            // 255 is how ffmpeg reports being stopped by a signal (and Windows reports a Ctrl+C style stop as a large code);
            // it still finishes the file, so only a real failure is reported.
            if (process.ExitCode is not (0 or 255 or -1 or unchecked((int)0xC000013A)))
            {
                var message = errors is null ? string.Empty : (await errors).Trim();
                var last = message.Length > 0 ? message.Split('\n')[^1].Trim() : $"exit code {process.ExitCode}";
                throw new InvalidOperationException($"ffmpeg ended with an error ({last}), so the file may be incomplete");
            }
        }

        if (file is null || !File.Exists(file) || new FileInfo(file).Length == 0)
        {
            throw new InvalidOperationException("ffmpeg produced no video file");
        }
    }
}

/// <summary>Finds ffmpeg, and the system's way of naming cameras to it.</summary>
public static class FfmpegCapture
{
    public static IFfmpegCapture ForCurrentOs() =>
        OperatingSystem.IsWindows() ? new DshowCapture()
        : OperatingSystem.IsLinux() ? new V4l2Capture()
        : new UnsupportedCapture();

    /// <summary>
    /// ffmpeg's location. The SAILRACING_FFMPEG environment variable wins (for a copy kept elsewhere); then the usual
    /// places: next to the program, the program's own folder under the user's data (Windows), and the PATH.
    /// </summary>
    public static string? Locate()
    {
        var overridePath = Environment.GetEnvironmentVariable("SAILRACING_FFMPEG");
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, exe) };
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(local, "SailRacing", "ffmpeg", exe));
            candidates.Add(Path.Combine(local, "Microsoft", "WinGet", "Links", exe));
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        candidates.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Select(dir => Path.Combine(dir, exe)));

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Runs ffmpeg to completion and returns everything it printed (used to list Windows cameras).</summary>
    internal static async Task<string> RunForOutputAsync(string ffmpeg, params string[] args)
    {
        var info = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("ffmpeg could not be started");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }

        return await output + "\n" + await errors;
    }
}

/// <summary>Not supported here (macOS): says so plainly.</summary>
internal sealed class UnsupportedCapture : IFfmpegCapture
{
    public string? FindFfmpeg() => null;

    public string? FontFile => null;

    public Task<IReadOnlyList<string>> InputArgumentsAsync(string ffmpeg, string? videoDeviceId, string? audioDeviceId, bool withAudio) =>
        throw new InvalidOperationException("recording video is supported on Windows and Linux");
}

/// <summary>Linux: the camera is a Video4Linux device (/dev/video0), the microphone comes through PulseAudio/PipeWire.</summary>
public sealed class V4l2Capture : IFfmpegCapture
{
    private static readonly string[] FontCandidates =
    {
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
        "/usr/share/fonts/truetype/noto/NotoSans-Bold.ttf"
    };

    public string? FindFfmpeg() => FfmpegCapture.Locate();

    public string? FontFile => FontCandidates.FirstOrDefault(File.Exists);

    public Task<IReadOnlyList<string>> InputArgumentsAsync(string ffmpeg, string? videoDeviceId, string? audioDeviceId, bool withAudio)
    {
        var device = string.IsNullOrWhiteSpace(videoDeviceId) ? FirstVideoDevice() : videoDeviceId;
        if (device is null)
        {
            throw new InvalidOperationException("no camera was found (there is no /dev/video0) — is the webcam plugged in?");
        }

        var args = new List<string> { "-f", "v4l2", "-i", device };
        if (withAudio)
        {
            args.AddRange(new[] { "-f", "pulse", "-i", string.IsNullOrWhiteSpace(audioDeviceId) ? "default" : audioDeviceId });
        }

        return Task.FromResult<IReadOnlyList<string>>(args);
    }

    private static string? FirstVideoDevice() =>
        Directory.Exists("/dev")
            ? Directory.GetFiles("/dev", "video*").OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault()
            : null;
}

/// <summary>
/// Windows: ffmpeg reads cameras and microphones through DirectShow, which names them by their display names. The
/// ones chosen in Settings (and every device ffmpeg can see) are matched by name; with no choice, the first is used.
/// </summary>
public sealed class DshowCapture : IFfmpegCapture
{
    public string? FindFfmpeg() => FfmpegCapture.Locate();

    public string? FontFile
    {
        get
        {
            var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            var bold = Path.Combine(fonts, "arialbd.ttf");
            return File.Exists(bold) ? bold : Path.Combine(fonts, "segoeuib.ttf") is var segoe && File.Exists(segoe) ? segoe : null;
        }
    }

    public async Task<IReadOnlyList<string>> InputArgumentsAsync(string ffmpeg, string? videoDeviceId, string? audioDeviceId, bool withAudio)
    {
        var listing = await FfmpegCapture.RunForOutputAsync(ffmpeg, "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy");
        var (videos, audios) = ParseDeviceList(listing);

        var video = Choose(videos, AppConfig.PreferredVideoInputDevice.Name)
            ?? throw new InvalidOperationException("no camera was found — is the webcam plugged in, and allowed in Windows' camera privacy settings?");

        var input = $"video={video}";
        if (withAudio)
        {
            var audio = Choose(audios, AppConfig.PreferredAudioInputDevice.Name)
                ?? throw new InvalidOperationException("no microphone was found");
            input += $":audio={audio}";
        }

        return new[] { "-f", "dshow", "-rtbufsize", "200M", "-i", input };
    }

    private static readonly Regex DeviceLine = new("\"(?<name>[^\"]+)\"\\s+\\((?<kind>video|audio|none)\\)", RegexOptions.Compiled);

    /// <summary>Reads the camera and microphone names from ffmpeg's "-list_devices" output (old and new layouts).</summary>
    public static (List<string> Videos, List<string> Audios) ParseDeviceList(string output)
    {
        var videos = new List<string>();
        var audios = new List<string>();

        foreach (var line in output.Split('\n'))
        {
            var match = DeviceLine.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value;
            switch (match.Groups["kind"].Value)
            {
                case "video" when !videos.Contains(name):
                    videos.Add(name);
                    break;
                case "audio" when !audios.Contains(name):
                    audios.Add(name);
                    break;
            }
        }

        // Older ffmpeg printed "DirectShow video devices" / "DirectShow audio devices" headings with unlabelled names.
        if (videos.Count == 0 && audios.Count == 0)
        {
            var section = string.Empty;
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains("DirectShow video devices")) section = "video";
                else if (line.Contains("DirectShow audio devices")) section = "audio";
                else if (section.Length > 0 && Regex.Match(line, "\"(?<name>[^\"]+)\"") is { Success: true } m && !line.Contains("Alternative name"))
                {
                    (section == "video" ? videos : audios).Add(m.Groups["name"].Value);
                }
            }
        }

        return (videos, audios);
    }

    /// <summary>The device matching the one chosen in Settings — exactly, then loosely — otherwise the first.</summary>
    public static string? Choose(IReadOnlyList<string> available, string? preferred)
    {
        if (available.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(preferred))
        {
            return available[0];
        }

        return available.FirstOrDefault(n => n.Equals(preferred, StringComparison.OrdinalIgnoreCase))
            ?? available.FirstOrDefault(n => n.Contains(preferred, StringComparison.OrdinalIgnoreCase) || preferred.Contains(n, StringComparison.OrdinalIgnoreCase))
            ?? available[0];
    }
}

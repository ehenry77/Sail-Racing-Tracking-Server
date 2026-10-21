using System.Text.RegularExpressions;
using SailRacing.Models;
using SailRacing.Services;
using SkiaSharp;
using Xunit;

namespace SailRacing.Core.Tests;

/// <summary>Feeds ffmpeg a generated test picture and tone instead of a camera, in real time, so a recording can be tested anywhere.</summary>
internal sealed class TestSrcCapture : IFfmpegCapture
{
    private readonly string _ffmpeg;

    public TestSrcCapture(string ffmpeg)
    {
        _ffmpeg = ffmpeg;
        FontFile = OperatingSystem.IsWindows() ? new DshowCapture().FontFile : new V4l2Capture().FontFile;
    }

    public string? FindFfmpeg() => _ffmpeg;

    public string? FontFile { get; }

    public Task<IReadOnlyList<string>> InputArgumentsAsync(string ffmpeg, string? videoDeviceId, string? audioDeviceId, bool withAudio)
    {
        var args = new List<string> { "-re", "-f", "lavfi", "-i", "testsrc=size=1280x720:rate=25" };
        if (withAudio)
        {
            args.AddRange(new[] { "-re", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100" });
        }

        return Task.FromResult<IReadOnlyList<string>>(args);
    }
}

public class FfmpegOverlayAndParsingTests
{
    [Fact]
    public void The_stamp_filter_has_a_band_a_caption_a_live_clock_and_a_gps_line_that_is_reread_each_frame()
    {
        var filter = FfmpegVideoRecorder.OverlayFilter(new VideoOverlay("/tmp/x/title.txt", "/tmp/x/gps.txt"), "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf");

        Assert.StartsWith("drawbox=", filter);
        Assert.Equal(3, Regex.Matches(filter, "drawtext=").Count);
        Assert.Contains("textfile=title.txt:reload=1", filter);
        Assert.Contains("textfile=gps.txt:reload=1", filter);
        Assert.Contains(@"%{localtime\:%Y-%m-%d} %{localtime\:%H}\:%{localtime\:%M}\:%{localtime\:%S}", filter);   // 24-hour, to the second
        Assert.Contains("(UTC", filter);
        Assert.DoesNotContain("%I", filter);        // never a 12-hour clock
        Assert.DoesNotContain("%p", filter);        // never AM/PM
        Assert.Contains("fontfile='/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'", filter);
    }

    [Theory]
    [InlineData(@"C:\Windows\Fonts\arialbd.ttf", @"'C\:/Windows/Fonts/arialbd.ttf'")]
    [InlineData(@"D:\My Fonts\bold.ttf", @"'D\:/My Fonts/bold.ttf'")]
    [InlineData("/usr/share/fonts/a.ttf", "'/usr/share/fonts/a.ttf'")]
    [InlineData("/home/o'brien/a.ttf", @"'/home/o'\''brien/a.ttf'")]
    public void Paths_are_escaped_for_the_filter(string path, string expected) =>
        Assert.Equal(expected, FfmpegVideoRecorder.EscapeFilterValue(path));

    [Fact]
    public void The_filter_works_without_a_font_file_too()
    {
        var filter = FfmpegVideoRecorder.OverlayFilter(new VideoOverlay("/a/title.txt", "/a/gps.txt"), null);
        Assert.DoesNotContain("fontfile", filter);
    }

    private const string NewLayout = """
        [dshow @ 000001e8a7a5a000] "Integrated Camera" (video)
        [dshow @ 000001e8a7a5a000]   Alternative name "@device_pnp_\\?\usb#vid_0c45&pid_6a10#6&2f3a#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global"
        [dshow @ 000001e8a7a5a000] "OBS Virtual Camera" (video)
        [dshow @ 000001e8a7a5a000]   Alternative name "@device_sw_{860BB310-5D01-11D0-BD3B-00A0C911CE86}\{A3FCE0F5-3493-419F-958A-ABA1250EC20B}"
        [dshow @ 000001e8a7a5a000] "Microphone Array (Realtek(R) Audio)" (audio)
        [dshow @ 000001e8a7a5a000]   Alternative name "@device_cm_{33D9A762-90C8-11D0-BD43-00A0C911CE86}\wave_{4E0A8A0C-2D35-4F2D-8C37-3F4B5C6B1C21}"
        dummy: Immediate exit requested
        """;

    private const string OldLayout = """
        [dshow @ 0000000002b7a7c0] DirectShow video devices (some may be both video and audio devices)
        [dshow @ 0000000002b7a7c0]  "Integrated Camera"
        [dshow @ 0000000002b7a7c0]     Alternative name "@device_pnp_\\?\usb#vid_0c45"
        [dshow @ 0000000002b7a7c0] DirectShow audio devices
        [dshow @ 0000000002b7a7c0]  "Microphone (Realtek High Definition Audio)"
        [dshow @ 0000000002b7a7c0]     Alternative name "@device_cm_{33D9A762}"
        dummy: Immediate exit requested
        """;

    [Fact]
    public void Camera_and_microphone_names_are_read_from_both_layouts_of_ffmpegs_device_list()
    {
        var (videos, audios) = DshowCapture.ParseDeviceList(NewLayout);
        Assert.Equal(new[] { "Integrated Camera", "OBS Virtual Camera" }, videos);
        Assert.Equal(new[] { "Microphone Array (Realtek(R) Audio)" }, audios);

        var (oldVideos, oldAudios) = DshowCapture.ParseDeviceList(OldLayout);
        Assert.Equal(new[] { "Integrated Camera" }, oldVideos);
        Assert.Equal(new[] { "Microphone (Realtek High Definition Audio)" }, oldAudios);

        var (none, noneAudio) = DshowCapture.ParseDeviceList("[dshow @ 000] dummy: Immediate exit requested");
        Assert.Empty(none);
        Assert.Empty(noneAudio);
    }

    [Fact]
    public void The_device_chosen_in_settings_is_found_exactly_then_loosely_else_the_first_is_used()
    {
        var cams = new[] { "Integrated Camera", "OBS Virtual Camera" };
        Assert.Equal("OBS Virtual Camera", DshowCapture.Choose(cams, "obs virtual camera"));
        Assert.Equal("OBS Virtual Camera", DshowCapture.Choose(cams, "OBS"));                            // loosely
        Assert.Equal("Integrated Camera", DshowCapture.Choose(cams, null));                             // nothing chosen
        Assert.Equal("Integrated Camera", DshowCapture.Choose(cams, "A camera that is unplugged"));     // the first still records
        Assert.Null(DshowCapture.Choose(Array.Empty<string>(), "x"));

        // Windows names a microphone slightly differently from DirectShow: "Realtek(R)" vs "Realtek".
        var mics = new[] { "Microphone Array (Realtek(R) Audio)" };
        Assert.Equal("Microphone Array (Realtek(R) Audio)", DshowCapture.Choose(mics, "Microphone Array (Realtek Audio)"));
    }
}

/// <summary>Real ffmpeg, generated pictures: set SR_FFMPEG to an ffmpeg executable to run these (they do nothing otherwise).</summary>
public class RealFfmpegTests : IDisposable
{
    private static readonly string? Ffmpeg = Environment.GetEnvironmentVariable("SR_FFMPEG") is { Length: > 0 } p && File.Exists(p) ? p : null;
    private static readonly string? Samples = Environment.GetEnvironmentVariable("SR_VIDEO_SAMPLES");

    private readonly TempFiles _files = new();
    private readonly ISettingsStore _previousStore = AppConfig.Store;

    public RealFfmpegTests() => AppConfig.Store = new InMemorySettingsStore();

    public void Dispose()
    {
        AppConfig.Store = _previousStore;
        _files.Dispose();
    }

    private static async Task<string> FfmpegAsync(params string[] args) => await FfmpegCapture.RunForOutputAsync(Ffmpeg!, args);

    private static int StampFolders() => Directory.GetDirectories(Path.GetTempPath(), "sailracing-video-*").Length;

    [Fact]
    public async Task A_recording_has_the_stamp_drawn_on_the_picture_and_a_subtitle_file_with_the_same_words()
    {
        if (Ffmpeg is null) return;

        var race = new Race { Name = "Autumn Cup" };
        var service = new StartVideoService(
            new FfmpegVideoRecorder(new TestSrcCapture(Ffmpeg)), _files, new MemoryRaces(race), new FakeLocation());
        var foldersBefore = StampFolders();

        var started = await service.StartAsync(race.Id, race.Name, new GeoFix(46.454, 6.5515), "finish line");
        Assert.True(started.Success, started.Message);
        Assert.True(service.HasSound);

        // With text drawing available the stamp goes on the picture; without it (some static ffmpeg builds) the video is still
        // recorded, the committee is told, and the subtitle file carries the same words.
        var canDraw = await FfmpegVideoRecorder.CanDrawTextAsync(Ffmpeg);
        Assert.Equal(canDraw, service.HasOverlay);
        Assert.Contains(canDraw ? "time and GPS on the picture" : "subtitle file only", started.Message);
        if (!canDraw)
        {
            Assert.Contains("cannot draw text", started.Message);
        }

        await Task.Delay(TimeSpan.FromSeconds(4.5));
        var stopped = await service.StopAsync();
        Assert.True(stopped.Success, stopped.Message);

        var video = stopped.Path!;
        Assert.True(File.Exists(video) && new FileInfo(video).Length > 10_000, "the video file is missing or empty");
        Assert.Equal(foldersBefore, StampFolders());   // the temporary text files are gone

        // The video is real: a few seconds long (ffmpeg's 2 s start-up check plus the 4.5 s waited), with a picture and sound.
        var info = await FfmpegAsync("-hide_banner", "-i", video);
        var duration = Regex.Match(info, @"Duration: (\d+):(\d+):(\d+\.\d+)");
        Assert.True(duration.Success, info);
        var seconds = int.Parse(duration.Groups[2].Value) * 60 + double.Parse(duration.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(seconds, 4.0, 10.0);
        Assert.Contains("Video: h264", info);
        Assert.Contains("Audio: aac", info);

        // The stamp is on the picture — if this ffmpeg can draw text at all (some static builds cannot).
        if (canDraw)
        {
            await AssertStampOnPictureAsync(video);
        }

        // The subtitle file beside the video: one entry a second, 24-hour time and the GPS of the finish line.
        var srt = Path.ChangeExtension(video, ".srt");
        Assert.True(File.Exists(srt));
        var text = await File.ReadAllTextAsync(srt);
        var times = Regex.Matches(text, @"\d{4}-\d{2}-\d{2} (\d{2}):(\d{2}):(\d{2}) \(UTC[+-]\d{2}:\d{2}\)");
        Assert.InRange(times.Count, 3, 10);
        Assert.All(times.Cast<Match>(), m => Assert.InRange(int.Parse(m.Groups[1].Value), 0, 23));
        Assert.DoesNotContain("AM", text);
        Assert.DoesNotContain("PM", text);
        Assert.Contains("GPS: 46.45400° N, 6.55150° E  (finish line)", text);
        Assert.Matches(@"00:00:00,\d{3} --> 00:00:01,\d{3}", text);   // one entry a second, timed from the start of the recording
        Assert.Matches(@"00:00:0[1-4],\d{3} --> 00:00:0[2-5],\d{3}", text);       // and the next entries follow, a second apart (give or take a slow moment)
    }

    private async Task AssertStampOnPictureAsync(string video)
    {
        // The stamp is on the picture: take a frame from the middle and look at it.
        var frame = Path.Combine(_files.DocumentsDirectory, "frame.png");
        await FfmpegAsync("-hide_banner", "-loglevel", "error", "-y", "-ss", "2.5", "-i", video, "-frames:v", "1", frame);
        Assert.True(File.Exists(frame), "no frame could be taken from the video");
        if (!string.IsNullOrEmpty(Samples))
        {
            Directory.CreateDirectory(Samples);
            File.Copy(frame, Path.Combine(Samples, $"video-frame-{(OperatingSystem.IsWindows() ? "windows" : "linux")}.png"), overwrite: true);
        }

        using var picture = SKBitmap.Decode(frame);
        Assert.Equal((1280, 720), (picture.Width, picture.Height));

        float Luma(int x, int y) => TestImages.Luma(picture.GetPixel(x, y));
        float Mean(int y0, int y1)
        {
            double sum = 0;
            var n = 0;
            for (var y = y0; y < y1; y += 2)
            {
                for (var x = 700; x < 1260; x += 4)
                {
                    sum += Luma(x, y);
                    n++;
                }
            }

            return (float)(sum / n);
        }

        // Right of the text, the dark band along the bottom is clearly darker than the picture above it.
        Assert.True(Mean(640, 700) < Mean(560, 620) * 0.7f, "no dark band along the bottom");

        // And the white text is on it: many bright pixels in the lower left, none of them in the picture above the band.
        var bright = 0;
        for (var y = 610; y < 715; y++)
        {
            for (var x = 0; x < 650; x++)
            {
                if (Luma(x, y) > 0.85f) bright++;
            }
        }

        Assert.True(bright > 800, $"expected white text on the band, found {bright} bright pixels");
    }

    [Fact]
    public async Task The_gps_line_on_the_picture_follows_this_pcs_position_as_it_changes()
    {
        if (Ffmpeg is null) return;

        var race = new Race { Name = "Moving" };
        var position = new GeoFix(47.0, 7.0);
        var calls = 0;
        var location = new FakeLocation
        {
            IsAvailable = true,
            Get = async () =>
            {
                // The first fix takes a moment, as a real GPS does; the race's position is shown until it arrives.
                if (Interlocked.Increment(ref calls) == 1) await Task.Delay(1500);
                return position;
            }
        };
        var timings = new StartVideoTimings(TimeSpan.FromSeconds(63), TimeSpan.FromSeconds(12), GpsEvery: TimeSpan.FromSeconds(1));
        var service = new StartVideoService(new FfmpegVideoRecorder(new TestSrcCapture(Ffmpeg)), _files, new MemoryRaces(race), location, timings);

        var started = await service.StartAsync(race.Id, race.Name, new GeoFix(46.0, 6.0), "finish line");
        Assert.True(started.Success, started.Message);
        await Task.Delay(TimeSpan.FromSeconds(4));            // the first fix arrives, then the committee boat moves
        position = new GeoFix(47.5, 7.5);
        await Task.Delay(TimeSpan.FromSeconds(3));
        var stopped = await service.StopAsync();
        Assert.True(stopped.Success, stopped.Message);

        var text = await File.ReadAllTextAsync(Path.ChangeExtension(stopped.Path!, ".srt"));
        Assert.True(text.Contains("GPS: 46.00000° N, 6.00000° E  (finish line)"), text);   // before the first fix
        Assert.True(text.Contains("GPS: 47.00000° N, 7.00000° E  (this PC)"), text);        // this PC's own position
        Assert.True(text.Contains("GPS: 47.50000° N, 7.50000° E  (this PC)"), text);        // ... and after it moved
    }

    [Fact]
    public async Task Without_a_stamp_ffmpeg_still_records_a_plain_video()
    {
        if (Ffmpeg is null) return;

        var recorder = new FfmpegVideoRecorder(new TestSrcCapture(Ffmpeg));
        var file = Path.Combine(_files.DocumentsDirectory, "plain.mp4");
        Directory.CreateDirectory(_files.DocumentsDirectory);

        var started = await recorder.StartAsync(file, null, null, withSound: false, overlay: null);
        Assert.False(started.HasOverlay);
        Assert.False(started.HasSound);
        await Task.Delay(1500);
        await recorder.StopAsync();

        Assert.True(new FileInfo(file).Length > 5000);
    }
}

public class FakeFfmpegOnLinuxTests
{
    /// <summary>A stand-in "ffmpeg": notes where it ran and its arguments, waits for "q" on its input like the real one, then writes the file.</summary>
    private static (string Folder, string Log) FakeFfmpeg(string extra = "")
    {
        var dir = Path.Combine(Path.GetTempPath(), "sr-fakeffmpeg-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var log = Path.Combine(dir, "args.log");
        // Asked which filters it has (before a stamped recording), it says it can draw text; that is not logged as a recording.
        var script = "#!/bin/sh\ncase \"$*\" in *-filters*) echo \" T.C drawtext V->V d\"; echo \" T.C drawbox V->V b\"; exit 0;; esac\n" +
                     $"echo \"cwd=$(pwd)\" >> \"{log}\"\necho \"$@\" >> \"{log}\"\n{extra}\nfor last; do :; done\nread line\necho video > \"$last\"\nexit 0\n";
        var path = Path.Combine(dir, "ffmpeg");
        File.WriteAllText(path, script);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return (dir, log);
    }

    private static async Task<T> WithPath<T>(string folder, Func<Task<T>> action, bool includeSystem = true)
    {
        var previous = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", includeSystem ? folder + Path.PathSeparator + "/usr/bin" + Path.PathSeparator + "/bin" : folder);
        try { return await action(); }
        finally { Environment.SetEnvironmentVariable("PATH", previous); }
    }

    private static string OutputFile() => Path.Combine(Path.GetTempPath(), "sr-video-" + Guid.NewGuid() + ".mp4");

    private static FfmpegVideoRecorder Recorder() => new(new V4l2Capture());

    [Fact]
    public async Task On_Linux_ffmpeg_records_with_sound_and_the_stamp_until_stopped()
    {
        if (!OperatingSystem.IsLinux()) return;

        var (folder, log) = FakeFfmpeg();
        var file = OutputFile();
        var overlayFolder = Path.Combine(Path.GetTempPath(), "sr-overlay-" + Guid.NewGuid());
        Directory.CreateDirectory(overlayFolder);
        var overlay = new VideoOverlay(Path.Combine(overlayFolder, "title.txt"), Path.Combine(overlayFolder, "gps.txt"));
        var recorder = Recorder();

        var started = await WithPath(folder, async () =>
        {
            var s = await recorder.StartAsync(file, "/dev/video-test", "alsa_input.mic", withSound: true, overlay);
            Assert.True(recorder.IsRecording);
            return s;
        });
        Assert.True(started.HasSound);
        Assert.True(started.HasOverlay);

        await recorder.StopAsync();
        Assert.False(recorder.IsRecording);
        Assert.True(File.Exists(file));

        var args = await File.ReadAllTextAsync(log);
        Assert.Contains($"cwd={overlayFolder}", args);                  // runs beside the text files, so they need no path
        Assert.Contains("-f v4l2 -i /dev/video-test", args);
        Assert.Contains("-f pulse -i alsa_input.mic", args);
        Assert.Contains("-vf drawbox", args);
        Assert.Contains("textfile=gps.txt:reload=1", args);
        Assert.Contains("-c:a aac", args);
        Assert.Contains("-t 3600", args);                                // the safeguard against a forgotten recording
        Assert.Contains("frag_keyframe", args);                          // playable even if cut off
        Assert.EndsWith(file + "\n", args);
    }

    [Fact]
    public async Task On_Linux_without_a_working_microphone_it_records_the_picture_alone()
    {
        if (!OperatingSystem.IsLinux()) return;

        var (folder, log) = FakeFfmpeg("case \"$*\" in *pulse*) echo \"Unknown input format: 'pulse'\" >&2; exit 1;; esac");
        var file = OutputFile();
        var recorder = Recorder();

        var started = await WithPath(folder, () => recorder.StartAsync(file, "/dev/video-test", null, withSound: true, overlay: null));

        Assert.False(started.HasSound);
        Assert.False(started.HasOverlay);
        Assert.True(recorder.IsRecording);
        await recorder.StopAsync();
        Assert.True(File.Exists(file));

        var launches = await File.ReadAllLinesAsync(log);
        var commands = launches.Where(l => !l.StartsWith("cwd=")).ToArray();
        Assert.Equal(2, commands.Length);
        Assert.Contains("pulse", commands[0]);
        Assert.DoesNotContain("pulse", commands[1]);
    }

    [Fact]
    public async Task On_Linux_a_camera_that_ffmpeg_cannot_open_gives_its_reason()
    {
        if (!OperatingSystem.IsLinux()) return;

        var dir = Path.Combine(Path.GetTempPath(), "sr-badffmpeg-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "ffmpeg");
        File.WriteAllText(path, "#!/bin/sh\necho 'Cannot open video device /dev/video-test: No such file or directory' >&2\nexit 1\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var recorder = Recorder();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WithPath(dir, () => recorder.StartAsync(OutputFile(), "/dev/video-test", null, withSound: false, overlay: null)));

        Assert.Contains("Cannot open video device", ex.Message);
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public async Task Without_ffmpeg_it_says_what_to_install()
    {
        if (!OperatingSystem.IsLinux()) return;

        var empty = Path.Combine(Path.GetTempPath(), "sr-empty-" + Guid.NewGuid());
        Directory.CreateDirectory(empty);

        var recorder = Recorder();
        Assert.False(recorder.IsAvailable);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            WithPath(empty, () => recorder.StartAsync(OutputFile(), "/dev/video-test", null, false, null), includeSystem: false));

        Assert.Contains("sudo apt install ffmpeg", ex.Message);
        Assert.Contains("winget install Gyan.FFmpeg", ex.Message);
    }
}

using System.Diagnostics;
using System.Globalization;
using SailRacing.Services;
using SkiaSharp;
using Xunit;

namespace SailRacing.Core.Tests;

internal static class TestImages
{
    /// <summary>A colourful JPEG (a gradient), so a caption drawn on it is clearly visible.</summary>
    public static byte[] Jpeg(int width = 1280, int height = 720)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint())
        {
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(width, height),
                new[] { new SKColor(70, 130, 180), new SKColor(250, 220, 120) }, null, SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, width, height, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();
    }

    public static float Luma(SKColor c) => (0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue) / 255f;
}

internal sealed class FakeCamera : ICameraService
{
    public byte[] Picture { get; set; } = TestImages.Jpeg();
    public Exception? Failure { get; set; }
    public string? RequestedDevice { get; private set; }
    public Task WarmUpAsync(string? deviceId) => Task.CompletedTask;
    public Task ReleaseAsync() => Task.CompletedTask;
    public Task<byte[]> CaptureJpegAsync(string? deviceId)
    {
        RequestedDevice = deviceId;
        return Failure is null ? Task.FromResult(Picture) : Task.FromException<byte[]>(Failure);
    }
}

internal sealed class FakeLocation : IGeolocationService
{
    public bool IsAvailable { get; set; }
    public Func<Task<GeoFix?>> Get { get; set; } = () => Task.FromResult<GeoFix?>(null);
    public Task<GeoFix?> GetLocationAsync(TimeSpan timeout) => Get();
}

internal sealed class TempFiles : IFileService, IDisposable
{
    public string DocumentsDirectory { get; } = Path.Combine(Path.GetTempPath(), "sr-photo-tests-" + Guid.NewGuid());
    public string CacheDirectory => DocumentsDirectory;
    public Task ShareFileAsync(string title, string path) => Task.CompletedTask;
    public Task<PickedTextFile?> PickTextFileAsync(string title, params string[] extensions) => Task.FromResult<PickedTextFile?>(null);
    public void Dispose()
    {
        try { Directory.Delete(DocumentsDirectory, true); } catch { /* temp folder */ }
    }
}

public class PhotoStampTests
{
    private static readonly string? SamplesDir = Environment.GetEnvironmentVariable("SR_PHOTO_SAMPLES");

    private static void SaveSample(string name, byte[] jpeg)
    {
        if (!string.IsNullOrEmpty(SamplesDir))
        {
            Directory.CreateDirectory(SamplesDir);
            File.WriteAllBytes(Path.Combine(SamplesDir, name), jpeg);
        }
    }

    [Fact]
    public void The_caption_is_drawn_on_a_band_along_the_bottom_and_the_rest_of_the_picture_is_untouched()
    {
        var original = TestImages.Jpeg();
        var request = new FinishPhotoRequest("Autumn Cup", "Lemanja", 3, 3, true, null, "");
        var lines = PhotoStamp.Lines(request, new DateTimeOffset(2026, 10, 20, 14, 32, 5, TimeSpan.FromHours(2)), new GeoFix(46.454, 6.5515), "this PC");

        var stamped = PhotoStamper.Stamp(original, lines);
        SaveSample("stamped-sample.jpg", stamped);

        using var before = SKBitmap.Decode(original);
        using var after = SKBitmap.Decode(stamped);
        Assert.Equal((before.Width, before.Height), (after.Width, after.Height));

        // Top of the picture: unchanged (to JPEG accuracy).
        Assert.True(Math.Abs(TestImages.Luma(before.GetPixel(400, 100)) - TestImages.Luma(after.GetPixel(400, 100))) < 0.04f);

        // Bottom band: clearly darker than the original, with bright text pixels on it.
        Assert.True(TestImages.Luma(after.GetPixel(1100, after.Height - 10)) < TestImages.Luma(before.GetPixel(1100, before.Height - 10)) * 0.6f);
        var bright = 0;
        for (var y = after.Height - 120; y < after.Height; y++)
        {
            for (var x = 0; x < 600; x++)
            {
                if (TestImages.Luma(after.GetPixel(x, y)) > 0.85f) bright++;
            }
        }

        Assert.True(bright > 200, $"expected white caption text on the band, found {bright} bright pixels");
    }

    [Fact]
    public void A_picture_that_cannot_be_read_gives_a_plain_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => PhotoStamper.Stamp(new byte[] { 1, 2, 3, 4 }, new[] { "x" }));
        Assert.Contains("could not be read", ex.Message);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-CH")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    public void Times_are_24_hour_in_every_regional_setting(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var evening = new DateTimeOffset(2026, 10, 20, 18, 7, 9, TimeSpan.FromHours(2));
            var afterMidnight = new DateTimeOffset(2026, 10, 21, 0, 5, 9, TimeSpan.FromHours(2));

            Assert.Equal("2026-10-20 18:07:09 (UTC+02:00)", PhotoStamp.FormatTimestamp(evening));
            Assert.Equal("2026-10-21 00:05:09 (UTC+02:00)", PhotoStamp.FormatTimestamp(afterMidnight));
            Assert.Equal("2026-10-20 07:30:00 (UTC-05:00)", PhotoStamp.FormatTimestamp(new DateTimeOffset(2026, 10, 20, 7, 30, 0, TimeSpan.FromHours(-5))));
            Assert.Equal("2026-10-20 09:15:00 (UTC+05:30)", PhotoStamp.FormatTimestamp(new DateTimeOffset(2026, 10, 20, 9, 15, 0, new TimeSpan(5, 30, 0))));

            Assert.Equal("46.45400° N, 6.55000° E", PhotoStamp.FormatCoordinates(46.454, 6.55));
            Assert.Equal("33.86000° S, 151.20000° W", PhotoStamp.FormatCoordinates(-33.86, -151.2));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void The_caption_names_the_boat_the_race_the_moment_and_the_place()
    {
        var at = new DateTimeOffset(2026, 10, 20, 14, 32, 5, TimeSpan.FromHours(2));

        var finish = PhotoStamp.Lines(new FinishPhotoRequest("Autumn Cup", "Lemanja", 3, 3, true, null, ""), at, new GeoFix(46.454, 6.5515), "finish line");
        Assert.Equal("Lemanja  —  Autumn Cup", finish[0]);
        Assert.Equal("FINISH   2026-10-20 14:32:05 (UTC+02:00)   laps 3/3", finish[1]);
        Assert.Equal("GPS: 46.45400° N, 6.55150° E  (finish line)", finish[2]);

        var manual = PhotoStamp.Lines(new FinishPhotoRequest("Autumn Cup", "Bise", 2, 3, false, null, ""), at, null, "");
        Assert.StartsWith("PHOTO   ", manual[1]);
        Assert.Equal("GPS: not available", manual[2]);
    }
}

public class FinishPhotoServiceTests
{
    private static FinishPhotoRequest Request(GeoFix? raceLocation = null, string label = "finish line") =>
        new("Cup: A/B", "Lemanja", 3, 3, true, raceLocation, label);

    [Fact]
    public async Task A_photo_is_stamped_saved_in_the_race_folder_and_named_by_boat_and_time()
    {
        using var files = new TempFiles();
        var camera = new FakeCamera();
        var service = new FinishPhotoService(camera, new FakeLocation(), files);

        var before = DateTimeOffset.Now;
        var result = await service.TakeAsync(Request(new GeoFix(46.454, 6.5515)));

        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(result.Path));
        Assert.Equal(Path.Combine(files.DocumentsDirectory, "Sail-Racing photos", "Cup_ A_B"), Path.GetDirectoryName(result.Path));
        Assert.Matches(@"^Lemanja \d{8}-\d{6}\.jpg$", Path.GetFileName(result.Path));

        using var saved = SKBitmap.Decode(await File.ReadAllBytesAsync(result.Path!));
        Assert.Equal((1280, 720), (saved.Width, saved.Height));

        // No GPS on this PC, so the race's finish line is what the photo says.
        Assert.Equal("GPS: 46.45400° N, 6.55150° E  (finish line)", service.LastCaption[2]);
        Assert.Contains("FINISH   " + before.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), service.LastCaption[1]);
    }

    [Fact]
    public async Task This_PCs_own_gps_is_preferred_to_the_finish_line()
    {
        using var files = new TempFiles();
        var location = new FakeLocation { IsAvailable = true, Get = () => Task.FromResult<GeoFix?>(new GeoFix(47.0, 7.0)) };
        var service = new FinishPhotoService(new FakeCamera(), location, files);

        var result = await service.TakeAsync(Request(new GeoFix(46.454, 6.5515)));

        Assert.True(result.Success, result.Message);
        Assert.Equal("GPS: 47.00000° N, 7.00000° E  (this PC)", service.LastCaption[2]);
    }

    [Fact]
    public async Task A_failing_or_missing_gps_never_costs_the_photo()
    {
        using var files = new TempFiles();
        var broken = new FakeLocation { IsAvailable = true, Get = () => throw new InvalidOperationException("location is switched off") };
        var service = new FinishPhotoService(new FakeCamera(), broken, files);

        var result = await service.TakeAsync(Request(new GeoFix(46.454, 6.5515)));
        Assert.True(result.Success, result.Message);
        Assert.Contains("(finish line)", service.LastCaption[2]);

        var none = await new FinishPhotoService(new FakeCamera(), new FakeLocation(), files).TakeAsync(Request(null, ""));
        Assert.True(none.Success);
    }

    [Fact]
    public async Task A_gps_that_never_answers_does_not_hold_the_photo_up()
    {
        using var files = new TempFiles();
        var hung = new FakeLocation { IsAvailable = true, Get = async () => { await Task.Delay(TimeSpan.FromSeconds(30)); return null; } };
        var service = new FinishPhotoService(new FakeCamera(), hung, files);

        var clock = Stopwatch.StartNew();
        var result = await service.TakeAsync(Request(new GeoFix(46.454, 6.5515)));

        Assert.True(result.Success, result.Message);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(8), $"took {clock.Elapsed}");
        Assert.Contains("(finish line)", service.LastCaption[2]);
    }

    [Fact]
    public async Task A_camera_problem_comes_back_as_a_message_not_an_exception()
    {
        using var files = new TempFiles();
        var camera = new FakeCamera { Failure = new InvalidOperationException("Windows is not letting this app use the camera") };

        var result = await new FinishPhotoService(camera, new FakeLocation(), files).TakeAsync(Request());

        Assert.False(result.Success);
        Assert.Null(result.Path);
        Assert.Contains("Windows is not letting this app use the camera", result.Message);
        Assert.False(Directory.Exists(Path.Combine(files.DocumentsDirectory, "Sail-Racing photos")), "no folder or file for a photo that was not taken");
    }

    [Fact]
    public async Task The_camera_chosen_in_settings_is_the_one_used()
    {
        using var files = new TempFiles();
        var previous = AppConfig.Store;
        try
        {
            AppConfig.Store = new InMemorySettingsStore();
            AppConfig.PreferredVideoInputDevice = ("camera-2", "Deck camera");
            var camera = new FakeCamera();

            await new FinishPhotoService(camera, new FakeLocation(), files).TakeAsync(Request());

            Assert.Equal("camera-2", camera.RequestedDevice);
        }
        finally
        {
            AppConfig.Store = previous;
        }
    }

    [Fact]
    public void The_automatic_finish_photo_is_off_until_switched_on()
    {
        var previous = AppConfig.Store;
        try
        {
            AppConfig.Store = new InMemorySettingsStore();
            Assert.False(AppConfig.AutoFinishPhoto);
            AppConfig.AutoFinishPhoto = true;
            Assert.True(AppConfig.AutoFinishPhoto);
            AppConfig.AutoFinishPhoto = false;
            Assert.False(AppConfig.AutoFinishPhoto);
        }
        finally
        {
            AppConfig.Store = previous;
        }
    }
}

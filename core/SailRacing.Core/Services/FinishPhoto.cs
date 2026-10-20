using System.Globalization;
using SkiaSharp;

namespace SailRacing.Services;

/// <summary>What a finish photo is of, for the caption stamped on it.</summary>
public sealed record FinishPhotoRequest(
    string RaceName,
    string BoatName,
    int LapsCompleted,
    int Laps,
    bool IsFinish,
    /// <summary>Where the race's finish line is, used for the GPS stamp when this PC has no location of its own.</summary>
    GeoFix? RaceLocation,
    string RaceLocationLabel);

public sealed record FinishPhotoResult(string? Path, string Message)
{
    public bool Success => Path is not null;
}

public interface IFinishPhotoService
{
    /// <summary>Opens the camera ahead of time so the photo at the finish is taken without delay. Never throws.</summary>
    Task WarmUpAsync();

    /// <summary>Closes the camera again. Never throws.</summary>
    Task ReleaseAsync();

    /// <summary>
    /// Takes a photo with the committee PC's camera, stamps it with the boat, the time (24-hour) and the GPS
    /// position, and saves it. Never throws: a problem comes back as a message.
    /// </summary>
    Task<FinishPhotoResult> TakeAsync(FinishPhotoRequest request);
}

/// <summary>
/// The finish photo: a still from the camera, captioned along the bottom with the boat, the race, the time of the
/// photo in 24-hour format and where it was taken (this PC's GPS if it has one, otherwise the race's finish line).
/// Saved under Documents / Sail-Racing photos / the race / "boat yyyyMMdd-HHmmss.jpg".
/// </summary>
public sealed class FinishPhotoService : IFinishPhotoService
{
    private static readonly TimeSpan LocationTimeout = TimeSpan.FromSeconds(3);

    private readonly ICameraService _camera;
    private readonly IGeolocationService _geolocation;
    private readonly IFileService _files;

    public FinishPhotoService(ICameraService camera, IGeolocationService geolocation, IFileService files)
    {
        _camera = camera;
        _geolocation = geolocation;
        _files = files;
    }

    /// <summary>The caption of the last photo taken — what a person would read on it.</summary>
    public IReadOnlyList<string> LastCaption { get; private set; } = Array.Empty<string>();

    public Task WarmUpAsync() => _camera.WarmUpAsync(AppConfig.PreferredVideoInputDevice.Id);

    public Task ReleaseAsync() => _camera.ReleaseAsync();

    public async Task<FinishPhotoResult> TakeAsync(FinishPhotoRequest request)
    {
        try
        {
            // Ask for the position while the camera works, so neither waits for the other.
            var locationTask = GetOwnLocationAsync();
            var jpeg = await _camera.CaptureJpegAsync(AppConfig.PreferredVideoInputDevice.Id);
            var takenAt = DateTimeOffset.Now;
            var own = await locationTask;

            var (fix, label) = own is not null ? (own, "this PC") : (request.RaceLocation, request.RaceLocationLabel);
            var caption = PhotoStamp.Lines(request, takenAt, fix, label);
            var stamped = PhotoStamper.Stamp(jpeg, caption);
            LastCaption = caption;

            var folder = Path.Combine(_files.DocumentsDirectory, "Sail-Racing photos", SafeName(request.RaceName, "race"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"{SafeName(request.BoatName, "boat")} {takenAt:yyyyMMdd-HHmmss}.jpg");
            await File.WriteAllBytesAsync(path, stamped);

            return new FinishPhotoResult(path, $"Photo saved: {path}");
        }
        catch (Exception ex)
        {
            return new FinishPhotoResult(null, $"Could not take the photo: {ex.Message}");
        }
    }

    private async Task<GeoFix?> GetOwnLocationAsync()
    {
        if (!_geolocation.IsAvailable)
        {
            return null;
        }

        try
        {
            return await _geolocation.GetLocationAsync(LocationTimeout).WaitAsync(LocationTimeout + TimeSpan.FromSeconds(1));
        }
        catch
        {
            return null; // no fix is not a reason to lose the photo
        }
    }

    // The same on every system, so a photos folder copied from a Linux PC to a Windows one (or onto a USB stick)
    // still opens: the characters Windows forbids in a file name are replaced everywhere, not just on Windows.
    private static readonly char[] ForbiddenInNames = { '<', '>', ':', '"', '/', (char)92, '|', '?', '*' }; // 92 is the backslash

    private static string SafeName(string name, string fallback)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        var cleaned = string.Concat(trimmed.Select(c => char.IsControl(c) || ForbiddenInNames.Contains(c) ? '_' : c))
            .TrimEnd('.', ' '); // Windows drops trailing dots and spaces
        return cleaned.Length == 0 ? fallback : cleaned;
    }
}

/// <summary>The words stamped on a photo. Times are always 24-hour, whatever the PC's regional setting.</summary>
public static class PhotoStamp
{
    public static IReadOnlyList<string> Lines(FinishPhotoRequest request, DateTimeOffset takenAt, GeoFix? fix, string fixLabel) => new[]
    {
        $"{request.BoatName}  —  {request.RaceName}",
        $"{(request.IsFinish ? "FINISH" : "PHOTO")}   {FormatTimestamp(takenAt)}   laps {request.LapsCompleted}/{request.Laps}",
        fix is null ? "GPS: not available" : $"GPS: {FormatCoordinates(fix.Latitude, fix.Longitude)}  ({fixLabel})"
    };

    /// <summary>"2026-10-20 14:32:05 (UTC+02:00)" — date, 24-hour time with seconds, and the offset it is local to.</summary>
    public static string FormatTimestamp(DateTimeOffset at)
    {
        var offset = at.Offset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
               $" (UTC{sign}{offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture)})";
    }

    /// <summary>"46.45400° N, 6.55150° E".</summary>
    public static string FormatCoordinates(double latitude, double longitude) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Math.Abs(latitude):0.00000}° {(latitude >= 0 ? 'N' : 'S')}, {Math.Abs(longitude):0.00000}° {(longitude >= 0 ? 'E' : 'W')}");
}

/// <summary>Draws the caption band onto a JPEG.</summary>
public static class PhotoStamper
{
    // Skia reports an unreadable picture as null or, for some inputs, with an exception; either way the caller
    // gets the same plain message.
    private static SKBitmap Decode(byte[] jpeg)
    {
        SKBitmap? bitmap = null;
        try
        {
            bitmap = SKBitmap.Decode(jpeg);
        }
        catch (Exception)
        {
            // handled below
        }

        return bitmap ?? throw new InvalidOperationException("the camera's picture could not be read");
    }

    public static byte[] Stamp(byte[] jpeg, IReadOnlyList<string> lines)
    {
        using var source = Decode(jpeg);

        // Work on a copy in a known pixel format, so drawing works whatever the decoder produced.
        using var bitmap = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.DrawBitmap(source, 0, 0);

            var textSize = Math.Max(14f, bitmap.Width / 42f);
            var lineHeight = textSize * 1.35f;
            var padding = textSize * 0.7f;
            var bandHeight = padding * 2 + lineHeight * lines.Count;

            using var band = new SKPaint { Color = new SKColor(0, 0, 0, 170), Style = SKPaintStyle.Fill };
            canvas.DrawRect(0, bitmap.Height - bandHeight, bitmap.Width, bandHeight, band);

            using var text = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
                TextSize = textSize,
                Typeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default
            };

            var y = bitmap.Height - bandHeight + padding + textSize;
            foreach (var line in lines)
            {
                canvas.DrawText(line, padding, y, text);
                y += lineHeight;
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }
}

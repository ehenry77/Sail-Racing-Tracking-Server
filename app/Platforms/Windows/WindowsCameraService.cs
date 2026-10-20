using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace SailRacing.Services;

/// <summary>
/// The webcam on Windows, through <see cref="MediaCapture"/>. Opening a webcam takes a second or two and its first
/// frames are dark while it settles its exposure, so when the finish photo is switched on the Timing Sheet warms the
/// camera up as it opens (<see cref="WarmUpAsync"/>) and a photo at the finish is then taken at once. Without a
/// warm-up the camera is opened, given a moment, used and closed again. The camera's own light is on whenever it
/// is open.
/// </summary>
public sealed class WindowsCameraService : ICameraService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MediaCapture? _capture;
    private string? _openDeviceId;
    private bool _keepOpen;

    public async Task WarmUpAsync(string? deviceId)
    {
        await _gate.WaitAsync();
        try
        {
            _keepOpen = true;
            await OpenAsync(deviceId);
        }
        catch
        {
            // The photo itself will report what is wrong (no camera, blocked...) when it is asked for.
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<byte[]> CaptureJpegAsync(string? deviceId)
    {
        await _gate.WaitAsync();
        try
        {
            var opened = _capture is null || _openDeviceId != (deviceId ?? string.Empty);
            try
            {
                await OpenAsync(deviceId);
                if (opened)
                {
                    await Task.Delay(800); // a freshly opened webcam's first frames are dark while it adjusts
                }

                using var stream = new InMemoryRandomAccessStream();
                await _capture!.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), stream);

                var bytes = new byte[stream.Size];
                using var reader = new DataReader(stream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
                return bytes;
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    "Windows is not letting this app use the camera — in Windows Settings > Privacy & security > Camera, " +
                    "allow camera access and \"Let desktop apps access your camera\".");
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw new InvalidOperationException($"the camera could not be used ({ex.Message.Trim()})");
            }
            finally
            {
                if (!_keepOpen)
                {
                    CloseCamera();
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReleaseAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _keepOpen = false;
            CloseCamera();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task OpenAsync(string? deviceId)
    {
        var wanted = deviceId ?? string.Empty;
        if (_capture is not null && _openDeviceId == wanted)
        {
            return;
        }

        CloseCamera();

        var settings = new MediaCaptureInitializationSettings { StreamingCaptureMode = StreamingCaptureMode.Video };
        if (wanted.Length > 0)
        {
            settings.VideoDeviceId = wanted;
        }

        var capture = new MediaCapture();
        try
        {
            await capture.InitializeAsync(settings);
        }
        catch
        {
            capture.Dispose();
            throw;
        }

        _capture = capture;
        _openDeviceId = wanted;
    }

    private void CloseCamera()
    {
        _capture?.Dispose();
        _capture = null;
        _openDeviceId = null;
    }
}

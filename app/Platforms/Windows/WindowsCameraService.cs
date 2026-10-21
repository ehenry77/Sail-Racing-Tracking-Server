using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SailRacing.Services;

/// <summary>
/// The webcam (and microphone) on Windows, through <see cref="MediaCapture"/>: still photos for the finish and the
/// video of the start. One class does both, so a photo and a video share the one open camera instead of competing
/// for it.
///
/// Opening a webcam takes a second or two and its first frames are dark while it settles its exposure, so the camera
/// is opened ahead of use — by the Timing Sheet for finish photos (<see cref="WarmUpAsync"/>), and shortly before the
/// last minute for the start video (<see cref="PrepareAsync"/>). Without that, a photo opens the camera, gives it a
/// moment, takes the picture and closes it again. The camera's own light is on whenever it is open.
/// </summary>
public sealed class WindowsCameraService : ICameraService, IVideoRecorder
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MediaCapture? _capture;
    private string _openVideoId = string.Empty;
    private bool _openWithAudio;
    private bool _keepOpenForPhotos;
    private bool _prepared;
    private bool _recording;

    public bool IsRecording => _recording;

    // ------------------------------------------------------------------------------------------ photos

    public async Task WarmUpAsync(string? deviceId)
    {
        await _gate.WaitAsync();
        try
        {
            _keepOpenForPhotos = true;
            await OpenAsync(deviceId, null, withAudio: false);
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
            var freshlyOpened = _capture is null || _openVideoId != (deviceId ?? string.Empty);
            try
            {
                await OpenAsync(deviceId, null, withAudio: false);
                if (freshlyOpened)
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
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                throw Friendly(ex);
            }
            finally
            {
                CloseIfUnused();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // ------------------------------------------------------------------------------------------ video

    public async Task PrepareAsync(string? videoDeviceId, string? audioDeviceId, bool withSound)
    {
        await _gate.WaitAsync();
        try
        {
            _prepared = true;
            try
            {
                await OpenAsync(videoDeviceId, audioDeviceId, withSound);
            }
            catch when (withSound)
            {
                await OpenAsync(videoDeviceId, null, withAudio: false); // no usable microphone: video only
            }
        }
        catch
        {
            // Start will try again and report the reason if it still cannot.
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<VideoStarted> StartAsync(string filePath, string? videoDeviceId, string? audioDeviceId, bool withSound, VideoOverlay? overlay)
    {
        await _gate.WaitAsync();
        try
        {
            if (_recording)
            {
                throw new InvalidOperationException("a video is already being recorded");
            }

            var hasSound = withSound;
            try
            {
                try
                {
                    await OpenAsync(videoDeviceId, audioDeviceId, withSound);
                }
                catch when (withSound)
                {
                    hasSound = false; // no usable microphone: record the picture without sound
                    await OpenAsync(videoDeviceId, null, withAudio: false);
                }

                var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(filePath)!);
                var file = await folder.CreateFileAsync(Path.GetFileName(filePath), CreationCollisionOption.ReplaceExisting);

                var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD720p);
                if (!_openWithAudio)
                {
                    profile.Audio = null;
                    hasSound = false;
                }

                await _capture!.StartRecordToStorageFileAsync(profile, file);
                _recording = true;
                _prepared = false;

                // MediaCapture writes straight to the file; there is no way to draw on the picture (the overlay is ignored).
                return new VideoStarted(hasSound, HasOverlay: false);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                CloseIfUnused();
                throw Friendly(ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_recording)
            {
                return;
            }

            try
            {
                await _capture!.StopRecordAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"the file may be incomplete ({ex.Message.Trim()})");
            }
            finally
            {
                _recording = false;
                CloseIfUnused();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    // ------------------------------------------------------------------------------------------ both

    /// <summary>Switches the camera off if nothing is waiting for it. Never while a video is being recorded.</summary>
    public async Task ReleaseAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _keepOpenForPhotos = false;
            _prepared = false;
            CloseIfUnused();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task OpenAsync(string? videoDeviceId, string? audioDeviceId, bool withAudio)
    {
        var wanted = videoDeviceId ?? string.Empty;

        // An open camera is reused when it is the right one and does whatever is wanted: with a microphone it can
        // also serve a photo, and while recording it must not be touched.
        if (_capture is not null && (_recording || (_openVideoId == wanted && (_openWithAudio || !withAudio))))
        {
            return;
        }

        CloseCamera();

        var settings = new MediaCaptureInitializationSettings
        {
            StreamingCaptureMode = withAudio ? StreamingCaptureMode.AudioAndVideo : StreamingCaptureMode.Video
        };
        if (wanted.Length > 0)
        {
            settings.VideoDeviceId = wanted;
        }

        if (withAudio && !string.IsNullOrEmpty(audioDeviceId))
        {
            settings.AudioDeviceId = audioDeviceId;
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
        _openVideoId = wanted;
        _openWithAudio = withAudio;
    }

    private void CloseIfUnused()
    {
        if (!_keepOpenForPhotos && !_prepared && !_recording)
        {
            CloseCamera();
        }
    }

    private void CloseCamera()
    {
        _capture?.Dispose();
        _capture = null;
        _openVideoId = string.Empty;
        _openWithAudio = false;
    }

    private static InvalidOperationException Friendly(Exception ex) => ex switch
    {
        UnauthorizedAccessException => new InvalidOperationException(
            "Windows is not letting this app use the camera or microphone — in Windows Settings > Privacy & security, " +
            "allow Camera and Microphone access, including \"Let desktop apps access your camera/microphone\"."),
        _ => new InvalidOperationException($"the camera could not be used ({ex.Message.Trim()})")
    };
}

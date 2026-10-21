namespace SailRacing.Services;

/// <summary>
/// The start video on Windows. With ffmpeg installed it records through ffmpeg, which can draw the time and GPS on the
/// picture. Without ffmpeg — or if ffmpeg cannot open the camera — it records with the Windows camera interface instead,
/// which cannot draw on the picture (the subtitle file beside the video still carries the time and GPS), and says so.
/// </summary>
public sealed class WindowsVideoRecorder : IVideoRecorder
{
    private readonly WindowsCameraService _windowsCamera;
    private readonly FfmpegVideoRecorder _ffmpeg = new(new DshowCapture());
    private IVideoRecorder? _active;

    public WindowsVideoRecorder(WindowsCameraService windowsCamera)
    {
        _windowsCamera = windowsCamera;
    }

    public bool IsRecording => _active?.IsRecording ?? false;

    // With ffmpeg there is nothing to warm up (it starts in a moment); the Windows camera interface does benefit.
    public Task PrepareAsync(string? videoDeviceId, string? audioDeviceId, bool withSound) =>
        _ffmpeg.IsAvailable ? Task.CompletedTask : _windowsCamera.PrepareAsync(videoDeviceId, audioDeviceId, withSound);

    public async Task<VideoStarted> StartAsync(string filePath, string? videoDeviceId, string? audioDeviceId, bool withSound, VideoOverlay? overlay)
    {
        string note;
        if (_ffmpeg.IsAvailable)
        {
            try
            {
                var started = await _ffmpeg.StartAsync(filePath, videoDeviceId, audioDeviceId, withSound, overlay);
                _active = _ffmpeg;
                return started;
            }
            catch (InvalidOperationException ex)
            {
                note = $"ffmpeg could not record ({ex.Message}), so the Windows camera recorder was used instead — without the time and GPS on the picture.";
            }
        }
        else
        {
            note = "For the time and GPS on the picture install ffmpeg (in a terminal: winget install Gyan.FFmpeg), then restart the app.";
        }

        var fallback = await _windowsCamera.StartAsync(filePath, videoDeviceId, audioDeviceId, withSound, overlay: null);
        _active = _windowsCamera;
        return fallback with { Note = note };
    }

    public Task StopAsync()
    {
        var active = _active;
        _active = null;
        return active?.StopAsync() ?? Task.CompletedTask;
    }

    public Task ReleaseAsync() => _windowsCamera.ReleaseAsync();
}

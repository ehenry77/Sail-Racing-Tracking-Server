using SailRacing.Services;

namespace SailRacing.Platform;

// The MAUI implementations of the interfaces in SailRacing.Core/Platform/PlatformServices.cs.

public sealed class MauiNavigator : INavigator
{
    public Task GoToAsync(string route) => Shell.Current.GoToAsync(route);
}

public sealed class MauiDialogService : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        Shell.Current.DisplayAlert(title, message, accept, cancel);

    public Task<string?> PromptAsync(string title, string message, string accept, string cancel, string? placeholder = null) =>
        Shell.Current.DisplayPromptAsync(title, message, accept, cancel, placeholder);
}

public sealed class MauiClipboardService : IClipboardService
{
    public Task SetTextAsync(string text) => Clipboard.Default.SetTextAsync(text);
}

public sealed class MauiLauncherService : ILauncherService
{
    public Task OpenAsync(Uri uri) => Launcher.Default.OpenAsync(uri);

    public Task OpenFileAsync(string path) =>
        Launcher.Default.OpenAsync(new OpenFileRequest(Path.GetFileName(path), new ReadOnlyFile(path)));
}

public sealed class MauiFileService : IFileService
{
    public string DocumentsDirectory => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public string CacheDirectory => FileSystem.CacheDirectory;

    public async Task ShareFileAsync(string title, string path)
    {
        try
        {
            await Share.Default.RequestAsync(new ShareFileRequest { Title = title, File = new ShareFile(path) });
        }
        catch
        {
            // The share sheet is a convenience; the file is already saved where the caller says.
        }
    }

    public async Task<PickedTextFile?> PickTextFileAsync(string title, params string[] extensions)
    {
        // One list per platform: Windows and macOS take extensions, Android a MIME type, iOS a type identifier.
        var type = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.iOS, new[] { "public.json" } },
            { DevicePlatform.Android, new[] { "application/json" } },
            { DevicePlatform.WinUI, extensions },
            { DevicePlatform.macOS, extensions.Select(e => e.TrimStart('.')).ToArray() },
            { DevicePlatform.MacCatalyst, new[] { "public.json" } }
        });

        var result = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = title, FileTypes = type });
        if (result is null)
        {
            return null;
        }

        await using var stream = await result.OpenReadAsync();
        using var reader = new StreamReader(stream);
        return new PickedTextFile(result.FileName, await reader.ReadToEndAsync());
    }
}

public sealed class MauiGeolocationService : IGeolocationService
{
    public bool IsAvailable => true;

    public async Task<GeoFix?> GetLocationAsync(TimeSpan timeout)
    {
        var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Best, timeout));
        return location is null ? null : new GeoFix(location.Latitude, location.Longitude);
    }
}

/// <summary>Settings in MAUI Preferences, so they survive from the version that stored them there directly.</summary>
public sealed class MauiSettingsStore : ISettingsStore
{
    public string? Get(string key) => Preferences.Default.Get<string?>(key, null);

    public void Set(string key, string? value)
    {
        if (value is null) Preferences.Default.Remove(key);
        else Preferences.Default.Set(key, value);
    }
}

public sealed class MauiAppPaths : IAppPaths
{
    public string DataDirectory => FileSystem.AppDataDirectory;
}

public sealed class MauiConnectivityService : IConnectivityService
{
    public MauiConnectivityService()
    {
        Connectivity.Current.ConnectivityChanged += (_, e) =>
            ConnectivityChanged?.Invoke(this, e.NetworkAccess == NetworkAccess.Internet);
    }

    public event EventHandler<bool>? ConnectivityChanged;
}

public sealed class MauiUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => MainThread.BeginInvokeOnMainThread(action);

    public IUiTimer CreateTimer() => new MauiUiTimer(Application.Current!.Dispatcher.CreateTimer());
}

internal sealed class MauiUiTimer : IUiTimer
{
    private readonly IDispatcherTimer _timer;

    public MauiUiTimer(IDispatcherTimer timer)
    {
        _timer = timer;
        _timer.Tick += (_, e) => Tick?.Invoke(this, e);
    }

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public event EventHandler? Tick;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();
}

/// <summary>The platform's own text-to-speech; Windows uses a faster dedicated one that falls back to this.</summary>
public sealed class MauiTextSpeechService : ISpeechService
{
    public Task PrepareAsync(IEnumerable<string> phrases) => Task.CompletedTask;

    public Task SpeakAsync(string text, CancellationToken token) =>
        TextToSpeech.Default.SpeakAsync(text, cancelToken: token);
}

/// <summary>
/// The camera on phones and tablets (and Mac Catalyst), through the system camera screen. There is no live camera to
/// warm up there: the system's camera app opens when a photo is asked for.
/// </summary>
public sealed class MauiCameraService : ICameraService
{
    public Task WarmUpAsync(string? deviceId) => Task.CompletedTask;

    public async Task<byte[]> CaptureJpegAsync(string? deviceId)
    {
        if (!MediaPicker.Default.IsCaptureSupported)
        {
            throw new InvalidOperationException("this device has no camera the app can use");
        }

        var photo = await MediaPicker.Default.CapturePhotoAsync()
            ?? throw new InvalidOperationException("no photo was taken");

        await using var stream = await photo.OpenReadAsync();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    public Task ReleaseAsync() => Task.CompletedTask;
}

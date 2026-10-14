using System.Diagnostics;
using System.Net.NetworkInformation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SailRacing.Services;

namespace SailRacing.Desktop.Platform;

/// <summary>The window the platform services attach their dialogs, clipboard and file pickers to.</summary>
internal static class DesktopHost
{
    public static Window? MainWindow { get; set; }
}

/// <summary>Opens links and folders with the desktop's own handlers (xdg-open, open, the shell).</summary>
internal static class ShellOpen
{
    public static void Open(string target)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { target }, UseShellExecute = false, RedirectStandardError = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { target }, UseShellExecute = false });
            }
            else
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
        }
        catch
        {
            // No handler installed: nothing useful to do from here.
        }
    }
}

public sealed class DesktopLauncherService : ILauncherService
{
    public Task OpenAsync(Uri uri)
    {
        ShellOpen.Open(uri.AbsoluteUri);
        return Task.CompletedTask;
    }
}

public sealed class DesktopClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        var clipboard = DesktopHost.MainWindow?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}

public sealed class DesktopFileService : IFileService
{
    public string DocumentsDirectory
    {
        get
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrEmpty(docs) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : docs;
        }
    }

    public string CacheDirectory => Path.GetTempPath();

    /// <summary>A desktop has no share sheet; the useful thing is to show the user where the file is.</summary>
    public Task ShareFileAsync(string title, string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = false });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-R", path }, UseShellExecute = false });
            }
            else
            {
                ShellOpen.Open(Path.GetDirectoryName(path) ?? path);
            }
        }
        catch
        {
            // The file is saved where the caller says; showing it is a convenience.
        }

        return Task.CompletedTask;
    }

    public async Task<PickedTextFile?> PickTextFileAsync(string title, params string[] extensions)
    {
        var storage = DesktopHost.MainWindow?.StorageProvider;
        if (storage is null)
        {
            return null;
        }

        var patterns = extensions.Select(e => "*" + (e.StartsWith('.') ? e : "." + e)).ToArray();
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Files") { Patterns = patterns } }
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            return null;
        }

        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        return new PickedTextFile(file.Name, await reader.ReadToEndAsync());
    }
}

/// <summary>
/// Linux has no standard location service, and a desktop PC rarely has a GPS. Buoys and the start line are
/// typed in (or captured with the Windows app on a phone/laptop that has one).
/// </summary>
public sealed class DesktopGeolocationService : IGeolocationService
{
    public bool IsAvailable => false;

    public Task<GeoFix?> GetLocationAsync(TimeSpan timeout) => Task.FromResult<GeoFix?>(null);
}

public sealed class DesktopAppPaths : IAppPaths
{
    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SailRacing");
}

public sealed class DesktopConnectivityService : IConnectivityService
{
    public DesktopConnectivityService()
    {
        NetworkChange.NetworkAvailabilityChanged += (_, e) => ConnectivityChanged?.Invoke(this, e.IsAvailable);
        // An address change (wifi reconnecting to another access point) also means "try again".
        NetworkChange.NetworkAddressChanged += (_, _) =>
        {
            if (NetworkInterface.GetIsNetworkAvailable())
            {
                ConnectivityChanged?.Invoke(this, true);
            }
        };
    }

    public event EventHandler<bool>? ConnectivityChanged;
}

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public IUiTimer CreateTimer() => new AvaloniaUiTimer();
}

internal sealed class AvaloniaUiTimer : IUiTimer
{
    private readonly DispatcherTimer _timer = new();

    public AvaloniaUiTimer()
    {
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

/// <summary>Modal question and text-entry dialogs over the main window.</summary>
public sealed class DesktopDialogService : IDialogService
{
    public async Task<bool> ConfirmAsync(string title, string message, string accept, string cancel)
    {
        var owner = DesktopHost.MainWindow;
        if (owner is null)
        {
            return false;
        }

        return await new DialogWindow(title, message, accept, cancel, withInput: false, placeholder: null).ShowAsync(owner) is not null;
    }

    public async Task<string?> PromptAsync(string title, string message, string accept, string cancel, string? placeholder = null)
    {
        var owner = DesktopHost.MainWindow;
        return owner is null
            ? null
            : await new DialogWindow(title, message, accept, cancel, withInput: true, placeholder).ShowAsync(owner);
    }
}

internal sealed class DialogWindow : Window
{
    private readonly TextBox? _input;
    private string? _result;

    public DialogWindow(string title, string message, string accept, string cancel, bool withInput, string? placeholder)
    {
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 14 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

        if (withInput)
        {
            _input = new TextBox { Watermark = placeholder };
            panel.Children.Add(_input);
        }

        var ok = new Button { Content = accept, IsDefault = true };
        var no = new Button { Content = cancel, IsCancel = true };
        ok.Click += (_, _) =>
        {
            _result = _input?.Text ?? string.Empty;
            Close();
        };
        no.Click += (_, _) => Close();

        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { no, ok }
        });
        Content = panel;

        Opened += (_, _) => _input?.Focus();
    }

    /// <summary>Shows the dialog and returns the entered text, or null when cancelled.</summary>
    public async Task<string?> ShowAsync(Window owner)
    {
        await ShowDialog(owner);
        return _result;
    }
}

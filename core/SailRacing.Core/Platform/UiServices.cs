namespace SailRacing.Services;

/// <summary>
/// The platform services a screen's view model may need, handed over as one bundle so every view model
/// takes a single constructor parameter for them instead of seven.
/// </summary>
public interface IUiServices
{
    INavigator Navigator { get; }

    IDialogService Dialogs { get; }

    IClipboardService Clipboard { get; }

    ILauncherService Launcher { get; }

    IFileService Files { get; }

    IGeolocationService Geolocation { get; }

    IUiDispatcher Dispatcher { get; }
}

public sealed class UiServices : IUiServices
{
    public UiServices(
        INavigator navigator,
        IDialogService dialogs,
        IClipboardService clipboard,
        ILauncherService launcher,
        IFileService files,
        IGeolocationService geolocation,
        IUiDispatcher dispatcher)
    {
        Navigator = navigator;
        Dialogs = dialogs;
        Clipboard = clipboard;
        Launcher = launcher;
        Files = files;
        Geolocation = geolocation;
        Dispatcher = dispatcher;
    }

    public INavigator Navigator { get; }

    public IDialogService Dialogs { get; }

    public IClipboardService Clipboard { get; }

    public ILauncherService Launcher { get; }

    public IFileService Files { get; }

    public IGeolocationService Geolocation { get; }

    public IUiDispatcher Dispatcher { get; }
}

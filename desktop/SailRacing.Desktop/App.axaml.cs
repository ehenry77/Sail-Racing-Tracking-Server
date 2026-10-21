using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SailRacing.Desktop.Platform;
using SailRacing.Desktop.Shell;
using SailRacing.Services;
using SailRacing.Services.Devices;

namespace SailRacing.Desktop;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        System.Diagnostics.Trace.WriteLine($"SailRacing: framework initialised, lifetime = {ApplicationLifetime?.GetType().Name ?? "none"}");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Services = BuildServices();
            System.Diagnostics.Trace.WriteLine("SailRacing: services built");

            var window = new MainWindow(Services.GetRequiredService<ScreenFactory>(), Services.GetRequiredService<ShellNavigator>());
            DesktopHost.MainWindow = window;
            desktop.MainWindow = window;

            // Closing the app while a start video is being recorded: finish the file properly rather than cut it off.
            desktop.Exit += (_, _) =>
            {
                try
                {
                    Task.Run(() => Services.GetRequiredService<IStartVideoService>().StopAsync()).Wait(TimeSpan.FromSeconds(10));
                }
                catch
                {
                    // exiting anyway
                }
            };
            System.Diagnostics.Trace.WriteLine("SailRacing: main window created");

            // Retry anything queued (races, manual timing) whenever the network comes back.
            Services.GetRequiredService<IRaceSyncService>().StartConnectivityWatcher();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The shared core plus this app's platform services and screens — the same wiring the MAUI app does.</summary>
    public static IServiceProvider BuildServices(IAppPaths? appPaths = null, Action<IServiceCollection>? configure = null)
    {
        var paths = appPaths ?? new DesktopAppPaths();
        AppConfig.Store = new JsonFileSettingsStore(Path.Combine(paths.DataDirectory, "settings.json"));

        var services = new ServiceCollection();

        services.AddSingleton<IAppPaths>(paths);
        services.AddSingleton<ShellNavigator>();
        services.AddSingleton<INavigator>(sp => sp.GetRequiredService<ShellNavigator>());
        services.AddSingleton<IDialogService, DesktopDialogService>();
        services.AddSingleton<IClipboardService, DesktopClipboardService>();
        services.AddSingleton<ILauncherService, DesktopLauncherService>();
        services.AddSingleton<IFileService, DesktopFileService>();
        services.AddSingleton<IGeolocationService, DesktopGeolocationService>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IConnectivityService, DesktopConnectivityService>();
        services.AddSingleton<ISpeechService, ProcessSpeechService>();
        services.AddSingleton<IDeviceSettingsService, DesktopDeviceSettingsService>();
        services.AddSingleton<ICameraService, DesktopCameraService>();
        services.AddSingleton<IVideoRecorder>(_ => new FfmpegVideoRecorder(FfmpegCapture.ForCurrentOs()));

        services.AddSailRacingCore();
        services.AddSingleton<ScreenFactory>();

        // Last, so a caller (the tests) can replace any registration.
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }
}

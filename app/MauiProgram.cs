using Microsoft.Extensions.Logging;
using SailRacing.Data;
using SailRacing.Platform;
using SailRacing.Services;
using SailRacing.Services.Devices;
using SailRacing.ViewModels;
using SailRacing.Views.Competitors;
using SailRacing.Views.Fleets;
using SailRacing.Views.Race;
using SailRacing.Views.Settings;

namespace SailRacing;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Platform services: how the shared core reaches MAUI's navigation, dialogs, files, location, settings...
        AppConfig.Store = new MauiSettingsStore();
        builder.Services.AddSingleton<INavigator, MauiNavigator>();
        builder.Services.AddSingleton<IDialogService, MauiDialogService>();
        builder.Services.AddSingleton<IClipboardService, MauiClipboardService>();
        builder.Services.AddSingleton<ILauncherService, MauiLauncherService>();
        builder.Services.AddSingleton<IFileService, MauiFileService>();
        builder.Services.AddSingleton<IGeolocationService, MauiGeolocationService>();
        builder.Services.AddSingleton<IUiDispatcher, MauiUiDispatcher>();
        builder.Services.AddSingleton<IAppPaths, MauiAppPaths>();
        builder.Services.AddSingleton<IConnectivityService, MauiConnectivityService>();

        // Everything shared with the desktop app: database, repositories, server client, sync, take-over,
        // start sequence and the view models.
        builder.Services.AddSailRacingCore();

        // Per-platform pieces the core needs.
#if WINDOWS
        builder.Services.AddSingleton<ISpeechService, WindowsSpeechService>();
#else
        builder.Services.AddSingleton<ISpeechService, MauiTextSpeechService>();
#endif
        builder.Services.AddSingleton<IDeviceSettingsService, DeviceSettingsService>();

        // Views
        builder.Services.AddTransient<CompetitorsPage>();
        builder.Services.AddTransient<FleetsPage>();
        builder.Services.AddTransient<RaceListPage>();
        builder.Services.AddTransient<RaceSetupPage>();
        builder.Services.AddTransient<StartSequencePage>();
        builder.Services.AddTransient<TimingSheetPage>();
        builder.Services.AddTransient<ResultsPage>();
        builder.Services.AddTransient<SettingsPage>();

        var app = builder.Build();

        var syncService = app.Services.GetRequiredService<IRaceSyncService>();
        syncService.StartConnectivityWatcher();

        return app;
    }
}

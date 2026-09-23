using Microsoft.Extensions.Logging;
using SailRacing.Data;
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

        // Data
        builder.Services.AddSingleton<ISailRacingDatabase, SailRacingDatabase>();
        builder.Services.AddSingleton<IParticipantRepository, ParticipantRepository>();
        builder.Services.AddSingleton<IFleetRepository, FleetRepository>();
        builder.Services.AddSingleton<IRaceRepository, RaceRepository>();
        builder.Services.AddSingleton<IResultPublicationRepository, ResultPublicationRepository>();

        // Services
        // Every screen (Race Setup, Start Sequence, Timing Sheet, Results) must stay fully usable off
        // local data when the server is unreachable — a short timeout means a down/unresponsive server
        // fails fast instead of leaving the UI hanging for HttpClient's 100-second default.
        builder.Services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(8) });
        builder.Services.AddSingleton<ISailRacingApiClient, SailRacingApiClient>();
        builder.Services.AddSingleton<IRaceSyncService, RaceSyncService>();
        builder.Services.AddTransient<IStartSequenceService, StartSequenceService>();
        builder.Services.AddTransient<IRaceSocketClient, RaceSocketClient>();
        builder.Services.AddSingleton<IDeviceSettingsService, DeviceSettingsService>();

        // ViewModels
        builder.Services.AddTransient<CompetitorsViewModel>();
        builder.Services.AddTransient<FleetsViewModel>();
        builder.Services.AddTransient<RaceListViewModel>();
        builder.Services.AddTransient<RaceSetupViewModel>();
        builder.Services.AddTransient<StartSequenceViewModel>();
        builder.Services.AddTransient<TimingSheetViewModel>();
        builder.Services.AddTransient<ResultsViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();

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

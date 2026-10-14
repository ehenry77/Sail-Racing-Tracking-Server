using Microsoft.Extensions.DependencyInjection;
using SailRacing.Data;
using SailRacing.Services;
using SailRacing.ViewModels;

namespace SailRacing;

public static class SailRacingCoreServices
{
    /// <summary>
    /// Registers everything the shared core provides — database, repositories, server client, sync, take-over,
    /// start sequence and the view models — so every app wires it up identically. Each app adds its own
    /// implementations of the interfaces in Platform/PlatformServices.cs, <see cref="ISpeechService"/> and
    /// <see cref="Services.Devices.IDeviceSettingsService"/>, plus its screens.
    /// </summary>
    public static IServiceCollection AddSailRacingCore(this IServiceCollection services)
    {
        services.AddSingleton<IUiServices, UiServices>();

        // Data
        services.AddSingleton<ISailRacingDatabase, SailRacingDatabase>();
        services.AddSingleton<IParticipantRepository, ParticipantRepository>();
        services.AddSingleton<IFleetRepository, FleetRepository>();
        services.AddSingleton<IRaceRepository, RaceRepository>();
        services.AddSingleton<IResultPublicationRepository, ResultPublicationRepository>();

        // Services
        // Every screen (Race Setup, Start Sequence, Timing Sheet, Results) must stay fully usable off
        // local data when the server is unreachable — a short timeout means a down/unresponsive server
        // fails fast instead of leaving the UI hanging for HttpClient's 100-second default.
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(8) });
        services.AddSingleton<ISailRacingApiClient, SailRacingApiClient>();
        services.AddSingleton<TimingOutbox>();
        services.AddSingleton<IRaceSyncService, RaceSyncService>();
        services.AddSingleton<IRaceTakeOverService, RaceTakeOverService>();
        // Singleton: one start sequence runs at a time, and it must survive leaving/re-entering the page.
        // Transient would let a re-entered page start a second timer while the first keeps announcing.
        services.AddSingleton<IStartSequenceService, StartSequenceService>();
        services.AddTransient<IRaceSocketClient, RaceSocketClient>();

        // View models
        services.AddTransient<CompetitorsViewModel>();
        services.AddTransient<FleetsViewModel>();
        services.AddTransient<RaceListViewModel>();
        services.AddTransient<RaceSetupViewModel>();
        services.AddTransient<StartSequenceViewModel>();
        services.AddTransient<TimingSheetViewModel>();
        services.AddTransient<ResultsViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}

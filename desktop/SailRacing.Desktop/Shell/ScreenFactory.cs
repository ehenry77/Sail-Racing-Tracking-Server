using Microsoft.Extensions.DependencyInjection;
using SailRacing.Desktop.Views;
using SailRacing.ViewModels;

namespace SailRacing.Desktop.Shell;

/// <summary>Builds a screen, with its view model, from the name used in navigation routes.</summary>
public sealed class ScreenFactory
{
    private readonly IServiceProvider _services;

    public ScreenFactory(IServiceProvider services)
    {
        _services = services;
    }

    /// <summary>The tabs along the top, in order; every other screen belongs to the Race tab.</summary>
    public static readonly string[] Tabs = { "competitors", "fleets", "races", "settings" };

    public static int TabOf(string name) => name switch
    {
        "competitors" => 0,
        "fleets" => 1,
        "settings" => 3,
        _ => 2
    };

    public ScreenView Create(string name, IReadOnlyDictionary<string, string> query)
    {
        query.TryGetValue("raceId", out var raceId);

        return name switch
        {
            "competitors" => new CompetitorsView(_services.GetRequiredService<CompetitorsViewModel>()),
            "fleets" => new FleetsView(_services.GetRequiredService<FleetsViewModel>()),
            "settings" => new SettingsView(_services.GetRequiredService<SettingsViewModel>()),
            "raceSetup" => new RaceSetupView(With(_services.GetRequiredService<RaceSetupViewModel>(), raceId)),
            "startSequence" => new StartSequenceView(With(_services.GetRequiredService<StartSequenceViewModel>(), raceId)),
            "timingSheet" => new TimingSheetView(With(_services.GetRequiredService<TimingSheetViewModel>(), raceId)),
            "results" => new ResultsView(With(_services.GetRequiredService<ResultsViewModel>(), raceId)),
            _ => new RaceListView(_services.GetRequiredService<RaceListViewModel>()) // "races" and anything unknown
        };
    }

    private static T With<T>(T viewModel, string? raceId) where T : IHasRaceId
    {
        viewModel.RaceId = raceId;
        return viewModel;
    }
}

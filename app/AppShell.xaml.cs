using SailRacing.Views.Race;

namespace SailRacing;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("raceSetup", typeof(RaceSetupPage));
        Routing.RegisterRoute("startSequence", typeof(StartSequencePage));
        Routing.RegisterRoute("timingSheet", typeof(TimingSheetPage));
        Routing.RegisterRoute("results", typeof(ResultsPage));
    }
}

using Avalonia.Controls;

namespace SailRacing.Desktop.Shell;

public partial class MainWindow : Window
{
    private readonly ScreenFactory _screens;
    private readonly RadioButton[] _tabButtons;

    public MainWindow()
        : this(null!, null!)
    {
        // Parameterless constructor for the designer/loader only.
    }

    public MainWindow(ScreenFactory screens, ShellNavigator navigator)
    {
        InitializeComponent();
        _screens = screens;
        _tabButtons = new[] { TabCompetitors, TabFleets, TabRace, TabSettings };

        if (screens is null)
        {
            return;
        }

        navigator.Attach(Show);

        // Each tab opens its own root screen; the Race tab goes back to the race list even from inside a race.
        for (var i = 0; i < _tabButtons.Length; i++)
        {
            var name = ScreenFactory.Tabs[i];
            _tabButtons[i].Click += (_, _) => Show(name, new Dictionary<string, string>());
        }

        Show("competitors", new Dictionary<string, string>());
    }

    /// <summary>Shows a screen by route name, and lights up the tab it belongs to.</summary>
    private void Show(string name, IReadOnlyDictionary<string, string> query)
    {
        _tabButtons[ScreenFactory.TabOf(name)].IsChecked = true;

        Host.Content = _screens.Create(name, query);
    }
}

using SailRacing.ViewModels;

namespace SailRacing.Desktop.Views;

public partial class SettingsView : ScreenView
{
    private readonly SettingsViewModel _viewModel;

    public SettingsView(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    protected override Task OnAppearingAsync() => _viewModel.LoadCommand.ExecuteAsync(null);
}

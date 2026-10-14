using SailRacing.ViewModels;

namespace SailRacing.Desktop.Views;

public partial class RaceSetupView : ScreenView
{
    private readonly RaceSetupViewModel _viewModel;

    public RaceSetupView(RaceSetupViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    protected override Task OnAppearingAsync() => _viewModel.OnAppearingAsync();
}

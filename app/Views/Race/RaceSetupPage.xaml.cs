using SailRacing.ViewModels;

namespace SailRacing.Views.Race;

public partial class RaceSetupPage : ContentPage
{
    private readonly RaceSetupViewModel _viewModel;

    public RaceSetupPage(RaceSetupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}

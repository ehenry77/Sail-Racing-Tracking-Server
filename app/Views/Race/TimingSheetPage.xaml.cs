using SailRacing.ViewModels;

namespace SailRacing.Views.Race;

public partial class TimingSheetPage : ContentPage
{
    private readonly TimingSheetViewModel _viewModel;

    public TimingSheetPage(TimingSheetViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}

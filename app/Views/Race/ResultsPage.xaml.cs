using SailRacing.ViewModels;

namespace SailRacing.Views.Race;

public partial class ResultsPage : ContentPage
{
    private readonly ResultsViewModel _viewModel;

    public ResultsPage(ResultsViewModel viewModel)
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

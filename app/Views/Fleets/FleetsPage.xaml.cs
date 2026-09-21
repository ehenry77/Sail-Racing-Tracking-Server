using SailRacing.ViewModels;

namespace SailRacing.Views.Fleets;

public partial class FleetsPage : ContentPage
{
    private readonly FleetsViewModel _viewModel;

    public FleetsPage(FleetsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}

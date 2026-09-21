using SailRacing.ViewModels;

namespace SailRacing.Views.Race;

public partial class RaceListPage : ContentPage
{
    private readonly RaceListViewModel _viewModel;

    public RaceListPage(RaceListViewModel viewModel)
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

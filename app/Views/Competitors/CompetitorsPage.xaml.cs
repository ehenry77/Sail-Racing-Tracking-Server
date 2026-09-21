using SailRacing.ViewModels;

namespace SailRacing.Views.Competitors;

public partial class CompetitorsPage : ContentPage
{
    private readonly CompetitorsViewModel _viewModel;

    public CompetitorsPage(CompetitorsViewModel viewModel)
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

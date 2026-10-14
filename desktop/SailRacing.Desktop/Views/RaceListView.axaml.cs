using SailRacing.ViewModels;

namespace SailRacing.Desktop.Views;

public partial class RaceListView : ScreenView
{
    private readonly RaceListViewModel _viewModel;

    public RaceListView(RaceListViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    protected override Task OnAppearingAsync() => _viewModel.LoadCommand.ExecuteAsync(null);
}

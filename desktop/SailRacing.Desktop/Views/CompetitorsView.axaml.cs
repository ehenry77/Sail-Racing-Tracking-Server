using SailRacing.ViewModels;

namespace SailRacing.Desktop.Views;

public partial class CompetitorsView : ScreenView
{
    private readonly CompetitorsViewModel _viewModel;

    public CompetitorsView(CompetitorsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    protected override Task OnAppearingAsync() => _viewModel.LoadCommand.ExecuteAsync(null);
}

using SailRacing.ViewModels;

namespace SailRacing.Desktop.Views;

public partial class ResultsView : ScreenView
{
    private readonly ResultsViewModel _viewModel;

    public ResultsView(ResultsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    protected override Task OnAppearingAsync() => _viewModel.OnAppearingAsync();
}

using SailRacing.ViewModels;

namespace SailRacing.Desktop.Views;

public partial class FleetsView : ScreenView
{
    private readonly FleetsViewModel _viewModel;

    public FleetsView(FleetsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    protected override Task OnAppearingAsync() => _viewModel.LoadCommand.ExecuteAsync(null);
}

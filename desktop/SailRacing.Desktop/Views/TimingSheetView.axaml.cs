using SailRacing.ViewModels;

namespace SailRacing.Desktop.Views;

public partial class TimingSheetView : ScreenView
{
    private readonly TimingSheetViewModel _viewModel;

    public TimingSheetView(TimingSheetViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    protected override Task OnAppearingAsync() => _viewModel.OnAppearingAsync();

    protected override void OnDisappearing() => _viewModel.OnDisappearing();
}

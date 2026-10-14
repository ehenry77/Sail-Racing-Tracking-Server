using SailRacing.ViewModels;

namespace SailRacing.Desktop.Views;

public partial class StartSequenceView : ScreenView
{
    private readonly StartSequenceViewModel _viewModel;

    public StartSequenceView(StartSequenceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
    }

    protected override Task OnAppearingAsync() => _viewModel.OnAppearingAsync();

    // Leaving the screen must not stop the sequence itself — it keeps counting down and announcing; only this
    // view model's screen hooks are released.
    protected override void OnDisappearing() => _viewModel.OnDisappearing();
}

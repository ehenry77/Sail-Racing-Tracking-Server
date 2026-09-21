using SailRacing.ViewModels;

namespace SailRacing.Views.Race;

public partial class StartSequencePage : ContentPage
{
    private readonly StartSequenceViewModel _viewModel;

    public StartSequencePage(StartSequenceViewModel viewModel)
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

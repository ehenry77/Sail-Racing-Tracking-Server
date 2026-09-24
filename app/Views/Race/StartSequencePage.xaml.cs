using System.ComponentModel;
using SailRacing.ViewModels;

namespace SailRacing.Views.Race;

public partial class StartSequencePage : ContentPage
{
    // Must match the flag stage Grid's RowDefinitions in the XAML (row1 + row2 heights): the distance
    // a flag card travels from its resting position (below the counter, row 2) to its hoisted position
    // (above the counter, row 0).
    private const double FlagTravelDistance = 200;

    private readonly StartSequenceViewModel _viewModel;

    public StartSequencePage(StartSequenceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();

        // Reflect whatever flag state we already know (e.g. resuming mid-sequence) without animating.
        SetFlagPosition(ClassFlagCard, _viewModel.ClassFlagUp, animate: false);
        SetFlagPosition(PFlagCard, _viewModel.PFlagUp, animate: false);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(StartSequenceViewModel.ClassFlagUp):
                SetFlagPosition(ClassFlagCard, _viewModel.ClassFlagUp, animate: true);
                break;
            case nameof(StartSequenceViewModel.PFlagUp):
                SetFlagPosition(PFlagCard, _viewModel.PFlagUp, animate: true);
                break;
        }
    }

    private static void SetFlagPosition(VisualElement flagCard, bool isUp, bool animate)
    {
        var targetY = isUp ? -FlagTravelDistance : 0;
        if (animate)
        {
            _ = flagCard.TranslateTo(0, targetY, 450, Easing.CubicInOut);
        }
        else
        {
            flagCard.TranslationY = targetY;
        }
    }
}

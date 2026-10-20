using System.ComponentModel;
using SailRacing.ViewModels;

namespace SailRacing.Views.Race;

[QueryProperty(nameof(RaceId), "raceId")]
public partial class StartSequencePage : ContentPage
{
    private readonly StartSequenceViewModel _viewModel;

    /// <summary>Set by Shell from the "raceId" query value before the page appears.</summary>
    public string? RaceId
    {
        get => _viewModel.RaceId;
        set => _viewModel.RaceId = value;
    }

    public StartSequencePage(StartSequenceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        // A fade interrupted by leaving the page must not leave a flag see-through.
        foreach (var flag in new VisualElement[] { ClassFlagUpView, ClassFlagDownView, PFlagUpView, PFlagDownView })
        {
            flag.Opacity = 1;
        }

        await _viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.OnDisappearing();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The flag that has just appeared — in the row above the counter when hoisted, below it when lowered —
        // fades in. Its visibility itself comes from the bindings in the XAML.
        switch (e.PropertyName)
        {
            case nameof(StartSequenceViewModel.ClassFlagUp):
                _ = FadeIn(_viewModel.ClassFlagUp ? ClassFlagUpView : ClassFlagDownView);
                break;
            case nameof(StartSequenceViewModel.PFlagUp):
                _ = FadeIn(_viewModel.PFlagUp ? PFlagUpView : PFlagDownView);
                break;
        }
    }

    private static async Task FadeIn(VisualElement flag)
    {
        flag.Opacity = 0;
        await flag.FadeTo(1, 450, Easing.CubicOut);
    }
}

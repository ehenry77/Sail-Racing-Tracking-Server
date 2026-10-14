using SailRacing.ViewModels;

namespace SailRacing.Views.Race;

[QueryProperty(nameof(RaceId), "raceId")]
public partial class TimingSheetPage : ContentPage
{
    private readonly TimingSheetViewModel _viewModel;

    /// <summary>Set by Shell from the "raceId" query value before the page appears.</summary>
    public string? RaceId
    {
        get => _viewModel.RaceId;
        set => _viewModel.RaceId = value;
    }

    public TimingSheetPage(TimingSheetViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}

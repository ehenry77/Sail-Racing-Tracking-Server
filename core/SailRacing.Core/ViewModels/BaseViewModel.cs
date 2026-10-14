using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SailRacing.Services;

namespace SailRacing.ViewModels;

public partial class BaseViewModel : ObservableObject
{
    protected BaseViewModel(IUiServices ui)
    {
        Ui = ui;
    }

    /// <summary>Navigation, dialogs, clipboard, files, location and the UI thread — implemented per app.</summary>
    protected IUiServices Ui { get; }

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string title = string.Empty;

    // Absolute route: returns straight to the Race tab's list and clears any pages pushed above it,
    // rather than relying on a platform back button that may not be shown.
    [RelayCommand]
    private async Task BackToRacesAsync() => await Ui.Navigator.GoToAsync("//races");
}

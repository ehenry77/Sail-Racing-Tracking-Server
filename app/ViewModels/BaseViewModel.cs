using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SailRacing.ViewModels;

public partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string title = string.Empty;

    // Absolute route: returns straight to the Race tab's list and clears any pages pushed above it,
    // rather than relying on a platform back button that may not be shown.
    [RelayCommand]
    private async Task BackToRacesAsync() => await Shell.Current.GoToAsync("//races");
}

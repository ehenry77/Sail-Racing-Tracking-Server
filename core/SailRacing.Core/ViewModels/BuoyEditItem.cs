using CommunityToolkit.Mvvm.ComponentModel;

namespace SailRacing.ViewModels;

public partial class BuoyEditItem : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [ObservableProperty]
    private int sequence;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string latitudeText = string.Empty;

    [ObservableProperty]
    private string longitudeText = string.Empty;

    [ObservableProperty]
    private bool capturedViaGps;
}

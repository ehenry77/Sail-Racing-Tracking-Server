using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SailRacing.Services;

namespace SailRacing.ViewModels;

/// <summary>
/// The screens that belong to one race (setup, start sequence, timing sheet, results) show the race's name and
/// its race code at the top. The code is what competitors' links, the live map and "Take over a race" use to
/// find the race; the server assigns it a moment after the race is first saved, so it may not be there at first.
/// </summary>
public abstract partial class RaceViewModelBase : BaseViewModel
{
    protected RaceViewModelBase(IUiServices ui) : base(ui)
    {
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRaceCode))]
    private string? raceCode;

    /// <summary>"Autumn Cup  ·  Race code K7P2M" — shown at the top of the screen.</summary>
    [ObservableProperty]
    private string raceHeaderText = "New race — its race code is assigned once it is saved";

    public bool HasRaceCode => !string.IsNullOrEmpty(RaceCode);

    /// <summary>Updates the header from the race's name and code; call again when the code arrives.</summary>
    protected void ShowRace(string? name, string? joinCode)
    {
        RaceCode = string.IsNullOrEmpty(joinCode) ? null : joinCode;

        var prefix = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim() + "  ·  ";
        RaceHeaderText = HasRaceCode
            ? $"{prefix}Race code {RaceCode}"
            : $"{prefix}Race code: assigned once the race has synced to the server";
    }

    /// <summary>Copies the race code, e.g. to type it into "Take over a race" on another PC.</summary>
    [RelayCommand]
    private async Task CopyRaceCodeAsync()
    {
        if (RaceCode is { } code)
        {
            await Ui.Clipboard.SetTextAsync(code);
        }
    }
}

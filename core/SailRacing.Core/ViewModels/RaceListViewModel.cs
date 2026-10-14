using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SailRacing.Data;
using SailRacing.Models;
using SailRacing.Services;

namespace SailRacing.ViewModels;

public partial class RaceListViewModel : BaseViewModel
{
    private readonly IRaceRepository _races;
    private readonly IRaceTakeOverService _takeOver;

    public ObservableCollection<Race> Races { get; } = new();

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public RaceListViewModel(IRaceRepository races, IRaceTakeOverService takeOver, IUiServices ui) : base(ui)
    {
        _races = races;
        _takeOver = takeOver;
        Title = "Races";
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var items = await _races.GetAllAsync();
            Races.Clear();
            foreach (var item in items)
            {
                Races.Add(item);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NewRaceAsync()
    {
        await Ui.Navigator.GoToAsync("raceSetup");
    }

    /// <summary>
    /// Continue a race that another committee PC started (or this one lost): pulls it from the server by its
    /// join code and opens it where it stands — start sequence, timing sheet or results.
    /// </summary>
    [RelayCommand]
    private async Task TakeOverAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var entered = await Ui.Dialogs.PromptAsync(
            "Take over a race",
            "Enter the race's join code, or paste one of its links. This PC then carries on from the race as the server holds it " +
            "— lap counts, finishes and the start time included.",
            "Take over",
            "Cancel",
            "e.g. K7P2M");
        if (string.IsNullOrWhiteSpace(entered))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Contacting the server…";
        try
        {
            var result = await _takeOver.TakeOverAsync(entered);
            StatusMessage = result.Message;
            if (!result.Success || result.Race is null)
            {
                return;
            }

            var items = await _races.GetAllAsync();
            Races.Clear();
            foreach (var item in items)
            {
                Races.Add(item);
            }

            await OpenAsync(result.Race);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenAsync(Race race)
    {
        var route = race.Status switch
        {
            RaceStatus.Setup => "raceSetup",
            RaceStatus.StartSequence => "startSequence",
            RaceStatus.Racing => "timingSheet",
            RaceStatus.Finished => "results",
            _ => "raceSetup"
        };

        await Ui.Navigator.GoToAsync($"{route}?raceId={race.Id}");
    }

    // Open routes by status (a running race goes to its timing sheet), so editing needs its own entry point.
    [RelayCommand]
    private async Task EditAsync(Race race)
    {
        await Ui.Navigator.GoToAsync($"raceSetup?raceId={race.Id}");
    }
}

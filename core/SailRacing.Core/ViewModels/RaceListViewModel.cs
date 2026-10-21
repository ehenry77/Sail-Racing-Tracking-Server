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
    private readonly IStartSequenceService _sequence;
    private readonly TimingOutbox _timingOutbox;
    private readonly IStartVideoService _video;

    public ObservableCollection<RaceListItem> Races { get; } = new();

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public RaceListViewModel(
        IRaceRepository races,
        IRaceTakeOverService takeOver,
        IStartSequenceService sequence,
        TimingOutbox timingOutbox,
        IStartVideoService video,
        IUiServices ui) : base(ui)
    {
        _video = video;
        _races = races;
        _takeOver = takeOver;
        _sequence = sequence;
        _timingOutbox = timingOutbox;
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
            await ReloadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAsync()
    {
        var items = await _races.GetAllAsync();
        Races.Clear();
        foreach (var item in items)
        {
            Races.Add(new RaceListItem(item));
        }
    }

    [RelayCommand]
    private async Task NewRaceAsync()
    {
        await Ui.Navigator.GoToAsync("raceSetup");
    }

    /// <summary>
    /// Continue a race that another committee PC started (or this one lost): pulls it from the server by its
    /// race code and opens it where it stands — start sequence, timing sheet or results.
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
            "Enter the race code, or paste one of the race's links. This PC then carries on from the race as the server holds it " +
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

            await ReloadAsync();
            await OpenAsync(result.Race);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Deletes a race from this PC after asking. Its copy on the server is deliberately left alone — the server has
    /// no way to tell a mistaken delete from a real one, and the live map, replay and every boat's link depend on it —
    /// so a deleted race can always be brought back with "Take over a race". Photos taken at the finish stay in the
    /// Documents folder. If this race's start sequence is the one running, it is stopped.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAsync(Race race)
    {
        if (IsBusy)
        {
            return;
        }

        var inProgress = race.Status is RaceStatus.StartSequence or RaceStatus.Racing;
        var message =
            (inProgress ? $"\"{race.Name}\" is in progress. " : string.Empty) +
            $"Delete \"{race.Name}\" from this PC, with its timing entries here?\n\n" +
            (string.IsNullOrEmpty(race.JoinCode)
                ? "It has not been sent to the server."
                : $"Its copy on the server is kept (live map, replay, boat links), and \"Take over a race\" with the race code {race.JoinCode} brings it back here.");

        if (!await Ui.Dialogs.ConfirmAsync("Delete this race?", message, "Delete race", "Keep it"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            // Only stop the sequence if it is this race's — another race may be the one counting down.
            if (_sequence.RaceId == race.Id && _sequence.IsRunning)
            {
                _sequence.Cancel();
            }

            if (_video.IsRecording && _video.RaceId == race.Id)
            {
                await _video.StopAsync();
            }

            await _timingOutbox.RemoveRaceAsync(race.Id);
            await _races.DeleteAsync(race);
            await ReloadAsync();

            StatusMessage = string.IsNullOrEmpty(race.JoinCode)
                ? $"Deleted \"{race.Name}\"."
                : $"Deleted \"{race.Name}\" from this PC. Its copy on the server was kept — Take over a race with code {race.JoinCode} brings it back.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not delete the race: {ex.Message}";
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

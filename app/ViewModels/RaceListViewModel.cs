using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using SailRacing.Data;
using SailRacing.Models;

namespace SailRacing.ViewModels;

public partial class RaceListViewModel : BaseViewModel
{
    private readonly IRaceRepository _races;

    public ObservableCollection<Race> Races { get; } = new();

    public RaceListViewModel(IRaceRepository races)
    {
        _races = races;
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
        await Shell.Current.GoToAsync("raceSetup");
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

        await Shell.Current.GoToAsync($"{route}?raceId={race.Id}");
    }
}

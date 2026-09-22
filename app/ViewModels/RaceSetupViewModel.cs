using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SailRacing.Data;
using SailRacing.Models;
using SailRacing.Services;

namespace SailRacing.ViewModels;

[QueryProperty(nameof(RaceId), "raceId")]
public partial class RaceSetupViewModel : BaseViewModel
{
    private readonly IRaceRepository _races;
    private readonly IFleetRepository _fleets;
    private readonly IParticipantRepository _participants;
    private readonly IRaceSyncService _sync;

    private Race _race = new();

    // Shell sets this via [QueryProperty] before OnAppearing runs, but only when navigating with a
    // "raceId" query param — for a brand-new race there is none, so loading happens explicitly from
    // OnAppearingAsync (called by the page) rather than as a side effect of this setter. That also
    // means the fleet list is refreshed every time the page appears, picking up fleets added elsewhere.
    public string? RaceId { get; set; }

    public ObservableCollection<Fleet> Fleets { get; } = new();

    public ObservableCollection<BuoyEditItem> Buoys { get; } = new();

    public ObservableCollection<RaceParticipantEditItem> RaceParticipants { get; } = new();

    [ObservableProperty]
    private Fleet? selectedFleet;

    [ObservableProperty]
    private string raceName = string.Empty;

    [ObservableProperty]
    private string lapsDefaultText = "3";

    [ObservableProperty]
    private string committeeLatitudeText = string.Empty;

    [ObservableProperty]
    private string committeeLongitudeText = string.Empty;

    [ObservableProperty]
    private string pinLatitudeText = string.Empty;

    [ObservableProperty]
    private string pinLongitudeText = string.Empty;

    [ObservableProperty]
    private bool finishSameAsStart = true;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public RaceSetupViewModel(
        IRaceRepository races,
        IFleetRepository fleets,
        IParticipantRepository participants,
        IRaceSyncService sync)
    {
        _races = races;
        _fleets = fleets;
        _participants = participants;
        _sync = sync;
        Title = "Race Setup";
    }

    public async Task OnAppearingAsync()
    {
        var fleets = await _fleets.GetAllAsync();
        Fleets.Clear();
        foreach (var fleet in fleets)
        {
            Fleets.Add(fleet);
        }

        if (!string.IsNullOrEmpty(RaceId))
        {
            var aggregate = await _races.GetAggregateAsync(RaceId);
            if (aggregate is not null)
            {
                _race = aggregate.Race;
                RaceName = _race.Name;
                LapsDefaultText = _race.LapsDefault.ToString();
                FinishSameAsStart = _race.FinishSameAsStart;
                CommitteeLatitudeText = FormatOrEmpty(aggregate.StartLine.CommitteeLatitude);
                CommitteeLongitudeText = FormatOrEmpty(aggregate.StartLine.CommitteeLongitude);
                PinLatitudeText = FormatOrEmpty(aggregate.StartLine.PinLatitude);
                PinLongitudeText = FormatOrEmpty(aggregate.StartLine.PinLongitude);

                Buoys.Clear();
                foreach (var buoy in aggregate.Buoys.OrderBy(b => b.Sequence))
                {
                    Buoys.Add(new BuoyEditItem
                    {
                        Id = buoy.Id,
                        Sequence = buoy.Sequence,
                        Name = buoy.Name,
                        LatitudeText = FormatOrEmpty(buoy.Latitude),
                        LongitudeText = FormatOrEmpty(buoy.Longitude),
                        CapturedViaGps = buoy.CapturedViaGps
                    });
                }

                SelectedFleet = Fleets.FirstOrDefault(f => f.Id == _race.FleetId);
                await LoadRaceParticipantsAsync(aggregate.RaceParticipants);
            }
        }
        else
        {
            _race = new Race();
        }
    }

    private async Task LoadRaceParticipantsAsync(List<Models.RaceParticipant>? existing = null)
    {
        RaceParticipants.Clear();
        if (SelectedFleet is null)
        {
            return;
        }

        var participantIds = await _fleets.GetParticipantIdsAsync(SelectedFleet.Id);
        var participants = await _participants.GetByIdsAsync(participantIds);

        foreach (var participant in participants)
        {
            var existingEntry = existing?.FirstOrDefault(e => e.ParticipantId == participant.Id);
            RaceParticipants.Add(new RaceParticipantEditItem
            {
                ParticipantId = participant.Id,
                ParticipantName = participant.Name,
                LapsText = (existingEntry?.Laps ?? int.Parse(string.IsNullOrWhiteSpace(LapsDefaultText) ? "3" : LapsDefaultText)).ToString()
            });
        }
    }

    partial void OnSelectedFleetChanged(Fleet? value)
    {
        _ = LoadRaceParticipantsAsync();
    }

    [RelayCommand]
    private async Task CaptureCommitteeGpsAsync()
    {
        var location = await CaptureGpsAsync();
        if (location is null)
        {
            return;
        }

        CommitteeLatitudeText = location.Latitude.ToString("0.######");
        CommitteeLongitudeText = location.Longitude.ToString("0.######");
    }

    [RelayCommand]
    private async Task CapturePinGpsAsync()
    {
        var location = await CaptureGpsAsync();
        if (location is null)
        {
            return;
        }

        PinLatitudeText = location.Latitude.ToString("0.######");
        PinLongitudeText = location.Longitude.ToString("0.######");
    }

    [RelayCommand]
    private void AddBuoy()
    {
        Buoys.Add(new BuoyEditItem { Sequence = Buoys.Count, Name = $"Mark {Buoys.Count + 1}" });
    }

    [RelayCommand]
    private void RemoveBuoy(BuoyEditItem buoy)
    {
        Buoys.Remove(buoy);
        for (var i = 0; i < Buoys.Count; i++)
        {
            Buoys[i].Sequence = i;
        }
    }

    [RelayCommand]
    private async Task CaptureBuoyGpsAsync(BuoyEditItem buoy)
    {
        var location = await CaptureGpsAsync();
        if (location is null)
        {
            return;
        }

        buoy.LatitudeText = location.Latitude.ToString("0.######");
        buoy.LongitudeText = location.Longitude.ToString("0.######");
        buoy.CapturedViaGps = true;
    }

    private async Task<Location?> CaptureGpsAsync()
    {
        try
        {
            StatusMessage = "Getting location...";
            var location = await Geolocation.Default.GetLocationAsync(
                new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(10)));
            StatusMessage = string.Empty;
            return location;
        }
        catch (Exception ex)
        {
            StatusMessage = $"GPS capture failed: {ex.Message}";
            return null;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(RaceName) || SelectedFleet is null)
        {
            StatusMessage = "Race name and fleet are required.";
            return;
        }

        IsBusy = true;
        try
        {
            _race.Name = RaceName.Trim();
            _race.FleetId = SelectedFleet.Id;
            _race.LapsDefault = int.TryParse(LapsDefaultText, out var laps) ? laps : 3;
            _race.FinishSameAsStart = FinishSameAsStart;

            var aggregate = new Data.RaceAggregate
            {
                Race = _race,
                StartLine = new StartLine
                {
                    RaceId = _race.Id,
                    CommitteeLatitude = ParseOrNull(CommitteeLatitudeText),
                    CommitteeLongitude = ParseOrNull(CommitteeLongitudeText),
                    PinLatitude = ParseOrNull(PinLatitudeText),
                    PinLongitude = ParseOrNull(PinLongitudeText)
                },
                Buoys = Buoys.Select(b => new Buoy
                {
                    Id = b.Id,
                    RaceId = _race.Id,
                    Sequence = b.Sequence,
                    Name = b.Name,
                    Latitude = ParseOrNull(b.LatitudeText),
                    Longitude = ParseOrNull(b.LongitudeText),
                    CapturedViaGps = b.CapturedViaGps
                }).ToList(),
                RaceParticipants = RaceParticipants.Select(rp => new Models.RaceParticipant
                {
                    RaceId = _race.Id,
                    ParticipantId = rp.ParticipantId,
                    Laps = int.TryParse(rp.LapsText, out var l) ? l : _race.LapsDefault
                }).ToList()
            };

            await _races.SaveAggregateAsync(aggregate);
            _race.SyncStatus = SyncStatus.NotSynced;
            await _races.SaveAggregateAsync(aggregate);

            RaceId = _race.Id;

            StatusMessage = "Saved locally. Syncing to server...";
            var synced = await _sync.PushAsync(_race.Id);
            StatusMessage = synced ? "Saved and synced." : "Saved locally; will retry sync automatically.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartSequenceAsync()
    {
        await SaveAsync();
        await Shell.Current.GoToAsync($"startSequence?raceId={_race.Id}");
    }

    private static double? ParseOrNull(string text) => double.TryParse(text, out var value) ? value : null;

    private static string FormatOrEmpty(double? value) => value?.ToString("0.######") ?? string.Empty;
}

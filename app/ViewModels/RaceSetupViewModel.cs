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

    // The race's existing per-boat entries (lap progress, status, finish times). Saving an edit must
    // update these in place — rebuilding them from the edit form would reset a running race's progress.
    private List<Models.RaceParticipant> _loadedRaceParticipants = new();

    // Setting SelectedFleet while loading an existing race must not trigger the user-driven reload.
    private bool _loadingRace;

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
    private bool hasScheduledStartTime;

    [ObservableProperty]
    private DateTime scheduledStartDate = DateTime.Today;

    [ObservableProperty]
    private TimeSpan scheduledStartTimeOfDay = RoundToNextMinute(DateTime.Now.TimeOfDay);

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

    /// <summary>False once the race has moved past setup — the "Save &amp; Start Sequence" shortcut no
    /// longer applies, but the race can still be edited and saved.</summary>
    [ObservableProperty]
    private bool isSetupStage = true;

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
                _loadedRaceParticipants = aggregate.RaceParticipants;
                IsSetupStage = _race.Status == RaceStatus.Setup;
                RaceName = _race.Name;
                LapsDefaultText = _race.LapsDefault.ToString();
                FinishSameAsStart = _race.FinishSameAsStart;

                if (_race.ScheduledStartTime is { } scheduled)
                {
                    var local = scheduled.ToLocalTime();
                    HasScheduledStartTime = true;
                    ScheduledStartDate = local.Date;
                    ScheduledStartTimeOfDay = local.TimeOfDay;
                }
                else
                {
                    HasScheduledStartTime = false;
                }
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

                _loadingRace = true;
                try
                {
                    SelectedFleet = Fleets.FirstOrDefault(f => f.Id == _race.FleetId);
                    await LoadRaceParticipantsAsync();
                }
                finally
                {
                    _loadingRace = false;
                }
            }
        }
        else
        {
            _race = new Race();
            _loadedRaceParticipants = new();
            IsSetupStage = true;
        }
    }

    private async Task LoadRaceParticipantsAsync()
    {
        RaceParticipants.Clear();
        if (SelectedFleet is null)
        {
            return;
        }

        var participantIds = await _fleets.GetParticipantIdsAsync(SelectedFleet.Id);
        var participants = await _participants.GetByIdsAsync(participantIds);

        // Clear again after the awaits: two overlapping loads would otherwise both add their rows.
        RaceParticipants.Clear();
        foreach (var participant in participants)
        {
            var existingEntry = _loadedRaceParticipants.FirstOrDefault(e => e.ParticipantId == participant.Id);
            RaceParticipants.Add(new RaceParticipantEditItem
            {
                ParticipantId = participant.Id,
                ParticipantName = participant.Name,
                JoinUrl = JoinLinks.Build(_race.JoinCode, participant.Id),
                LapsText = (existingEntry?.Laps ?? int.Parse(string.IsNullOrWhiteSpace(LapsDefaultText) ? "3" : LapsDefaultText)).ToString()
            });
        }
    }

    partial void OnSelectedFleetChanged(Fleet? value)
    {
        if (!_loadingRace)
        {
            _ = LoadRaceParticipantsAsync();
        }
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

            if (HasScheduledStartTime)
            {
                var local = ScheduledStartDate.Date + ScheduledStartTimeOfDay;
                _race.ScheduledStartTime = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
            }
            else
            {
                _race.ScheduledStartTime = null;
            }

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
                // Reuse each boat's existing entry and only change its lap target, so editing a race
                // that's already running keeps its lap counts, statuses and finish times.
                RaceParticipants = RaceParticipants.Select(rp =>
                {
                    var entry = _loadedRaceParticipants.FirstOrDefault(e => e.ParticipantId == rp.ParticipantId)
                                ?? new Models.RaceParticipant { ParticipantId = rp.ParticipantId };
                    entry.RaceId = _race.Id;
                    entry.Laps = int.TryParse(rp.LapsText, out var l) ? l : _race.LapsDefault;
                    return entry;
                }).ToList()
            };

            await _races.SaveAggregateAsync(aggregate);
            _race.SyncStatus = SyncStatus.NotSynced;
            await _races.SaveAggregateAsync(aggregate);
            _loadedRaceParticipants = aggregate.RaceParticipants;

            RaceId = _race.Id;
            StatusMessage = "Saved locally. Syncing to server…";
        }
        finally
        {
            IsBusy = false;
        }

        // Fire-and-forget: the local save above is already complete and durable, so Save must never
        // make the committee wait on the network — a down/unreachable server just means this resolves
        // later (RaceSyncService's connectivity watcher retries it automatically in the background).
        _ = SyncInBackgroundAsync(_race.Id);
    }

    private async Task SyncInBackgroundAsync(string raceId)
    {
        var synced = false;
        Race? fresh = null;
        try
        {
            synced = await _sync.PushAsync(raceId);
            if (synced)
            {
                fresh = (await _races.GetAggregateAsync(raceId))?.Race;
            }
        }
        catch
        {
            // Non-fatal: the race is saved locally and the connectivity watcher retries the push.
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (RaceId != raceId)
            {
                return;
            }

            if (fresh is not null)
            {
                // The sync stored the server-assigned join code on the database row. Copy it onto this
                // view model's own copy too — otherwise the next Save would write the stale (empty) values
                // back over it, and the race would be re-created on the server with a new code.
                _race.RemoteRaceId = fresh.RemoteRaceId;
                _race.JoinCode = fresh.JoinCode;
                _race.SyncStatus = fresh.SyncStatus;
                RefreshJoinLinks();
            }

            StatusMessage = synced ? "Saved and synced." : "Saved locally; will retry sync automatically.";
        });
    }

    private void RefreshJoinLinks()
    {
        foreach (var item in RaceParticipants)
        {
            item.JoinUrl = JoinLinks.Build(_race.JoinCode, item.ParticipantId);
        }
    }

    [RelayCommand]
    private async Task CopyJoinLinkAsync(RaceParticipantEditItem item)
    {
        if (!item.HasJoinUrl)
        {
            StatusMessage = "Join links appear once the race has synced to the server.";
            return;
        }

        await Clipboard.Default.SetTextAsync(item.JoinUrl);
        StatusMessage = $"Copied the join link for {item.ParticipantName}.";
    }

    [RelayCommand]
    private async Task StartSequenceAsync()
    {
        await SaveAsync();
        await Shell.Current.GoToAsync($"startSequence?raceId={_race.Id}");
    }

    private static double? ParseOrNull(string text) => double.TryParse(text, out var value) ? value : null;

    private static string FormatOrEmpty(double? value) => value?.ToString("0.######") ?? string.Empty;

    private static TimeSpan RoundToNextMinute(TimeSpan t) => TimeSpan.FromMinutes(Math.Ceiling(t.TotalMinutes));
}

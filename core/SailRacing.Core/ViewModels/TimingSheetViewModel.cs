using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SailRacing.Data;
using SailRacing.Models;
using SailRacing.Services;

namespace SailRacing.ViewModels;

public partial class TimingSheetViewModel : RaceViewModelBase, IHasRaceId, IDisposable
{
    private readonly IRaceRepository _races;
    private readonly IFleetRepository _fleets;
    private readonly IParticipantRepository _participants;
    private readonly IRaceSocketClient _socket;
    private readonly ISailRacingApiClient _api;
    private readonly IRaceSyncService _sync;
    private readonly IFinishPhotoService _photos;

    private readonly JoinCodeWatcher _joinWatcher;

    private Race? _race;

    // Where the finish line is, for the GPS stamp on a photo when this PC has no location of its own.
    private GeoFix? _raceLocation;
    private string _raceLocationLabel = string.Empty;

    // Boats whose photo is being taken right now, so a finish seen twice doesn't take two.
    private readonly HashSet<string> _photoInFlight = new();

    // When each boat was last edited by hand on this PC. The server echoes a manual entry back as events;
    // applying an echo of an earlier press while a later one is already on screen would flick the count back.
    private readonly Dictionary<string, DateTime> _manualAt = new();
    private static readonly TimeSpan EchoWindow = TimeSpan.FromSeconds(5);

    public string? RaceId { get; set; }

    public ObservableCollection<RaceTimingEntry> Entries { get; } = new();

    [ObservableProperty]
    private string connectionStatusText = string.Empty;

    [ObservableProperty]
    private bool hasConnectionIssue;

    [ObservableProperty]
    private string infoMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMapUrl))]
    [NotifyPropertyChangedFor(nameof(MapLinkText))]
    private string? mapUrl;

    [ObservableProperty]
    private string? replayUrl;

    public bool HasMapUrl => !string.IsNullOrEmpty(MapUrl);

    public string MapLinkText => MapUrl is null ? JoinLinks.MapNotSyncedText : $"Live map: {MapUrl}";

    /// <summary>The boat currently selected for manual lap/finish-time entry — the offline-capable
    /// fallback to the server's GPS-based auto lap-counting, e.g. when a competitor isn't tracking
    /// or the server is unreachable.</summary>
    [ObservableProperty]
    private RaceTimingEntry? selectedEntry;

    [ObservableProperty]
    private DateTime editFinishDate = DateTime.Today;

    [ObservableProperty]
    private TimeSpan editFinishTimeOfDay = DateTime.Now.TimeOfDay;

    partial void OnSelectedEntryChanged(RaceTimingEntry? value)
    {
        var local = (value?.FinishTime ?? DateTimeOffset.Now).ToLocalTime();
        EditFinishDate = local.Date;
        EditFinishTimeOfDay = local.TimeOfDay;
    }

    public TimingSheetViewModel(
        IRaceRepository races,
        IFleetRepository fleets,
        IParticipantRepository participants,
        IRaceSocketClient socket,
        ISailRacingApiClient api,
        IRaceSyncService sync,
        IFinishPhotoService photos,
        IUiServices ui) : base(ui)
    {
        _sync = sync;
        _photos = photos;
        _races = races;
        _fleets = fleets;
        _participants = participants;
        _socket = socket;
        _api = api;
        _joinWatcher = new JoinCodeWatcher(races, ui.Dispatcher);
        Title = "Timing Sheet";
    }

    private void OnEntryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RaceTimingEntry.LapsCompleted)
            or nameof(RaceTimingEntry.Status)
            or nameof(RaceTimingEntry.ElapsedSeconds)
            or nameof(RaceTimingEntry.FinishTime))
        {
            ResortEntries();
        }
    }

    /// <summary>
    /// Leaderboard order: finished boats first (first across the line at the top), then boats still
    /// racing by laps completed (most first), then boats that didn't finish (DNF/DNS/RET/OCS). Ties keep
    /// their current order, so rows don't swap places for no reason. Entries are moved in place rather
    /// than rebuilt so the list (and the selected boat) isn't reset on every lap.
    /// Finish order is by elapsed time; the Results page is what applies TCF to rank corrected time.
    /// </summary>
    private void ResortEntries()
    {
        var selected = SelectedEntry;

        var ordered = Entries
            .OrderBy(e => e.Status switch
            {
                RaceParticipantStatus.Finished => 0,
                RaceParticipantStatus.Racing => 1,
                _ => 2
            })
            .ThenBy(e => e.Status == RaceParticipantStatus.Finished ? e.ElapsedSeconds ?? double.MaxValue : 0)
            .ThenBy(e => e.Status == RaceParticipantStatus.Finished ? e.FinishTime ?? DateTimeOffset.MaxValue : DateTimeOffset.MinValue)
            .ThenByDescending(e => e.Status == RaceParticipantStatus.Racing ? e.LapsCompleted : 0)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var from = Entries.IndexOf(ordered[i]);
            if (from != i)
            {
                Entries.Move(from, i);
            }
        }

        // Moving rows can make the list drop its selection; keep the boat being edited open.
        if (selected is not null && !ReferenceEquals(SelectedEntry, selected))
        {
            SelectedEntry = selected;
        }
    }

    private void OnJoinCodeArrived(Race fresh)
    {
        foreach (var entry in Entries)
        {
            entry.JoinUrl = JoinLinks.Build(fresh.JoinCode, entry.ParticipantId);
        }

        MapUrl = JoinLinks.BuildMap(fresh.JoinCode);
        ReplayUrl = JoinLinks.BuildReplay(fresh.JoinCode);
        ShowRace(fresh.Name, fresh.JoinCode);
    }

    public async Task OnAppearingAsync()
    {
        // Listen only while the page is showing: with back-navigation the user can leave and re-enter,
        // and a lingering instance still receiving socket messages would PersistAsync its stale entries
        // over newer data (e.g. a finish time set manually in the new instance).
        _socket.MessageReceived -= OnMessageReceived;
        _socket.MessageReceived += OnMessageReceived;

        if (string.IsNullOrEmpty(RaceId))
        {
            return;
        }

        try
        {
            var aggregate = await _races.GetAggregateAsync(RaceId);
            if (aggregate is null)
            {
                return;
            }

            _race = aggregate.Race;
            (_raceLocation, _raceLocationLabel) = FinishLocationOf(aggregate);
            MapUrl = JoinLinks.BuildMap(_race.JoinCode);
            ReplayUrl = JoinLinks.BuildReplay(_race.JoinCode);
            ShowRace(_race.Name, _race.JoinCode);

            var participantIds = aggregate.RaceParticipants.Select(rp => rp.ParticipantId);
            var participants = await _participants.GetByIdsAsync(participantIds);

            Entries.Clear();
            foreach (var rp in aggregate.RaceParticipants)
            {
                var participant = participants.FirstOrDefault(p => p.Id == rp.ParticipantId);
                var entry = new RaceTimingEntry
                {
                    ParticipantId = rp.ParticipantId,
                    ParticipantName = participant?.Name ?? "(unknown)",
                    Tcf = participant?.Tcf ?? 1.0,
                    Laps = rp.Laps,
                    LapsCompleted = rp.LapsCompleted,
                    IsOnFinalLap = rp.IsOnFinalLap,
                    Status = rp.Status,
                    FinishTime = rp.FinishTime,
                    ElapsedSeconds = rp.ElapsedSeconds,
                    PhotoPath = rp.FinishPhotoPath,
                    JoinUrl = JoinLinks.Build(_race.JoinCode, rp.ParticipantId)
                };
                entry.PropertyChanged += OnEntryPropertyChanged;
                Entries.Add(entry);
            }

            ResortEntries();

            // With automatic finish photos on, open the camera now so the picture at the finish is immediate.
            if (AppConfig.AutoFinishPhoto)
            {
                _ = _photos.WarmUpAsync();
            }

            if (string.IsNullOrEmpty(_race.JoinCode))
            {
                _joinWatcher.Start(_race.Id, OnJoinCodeArrived);
            }

            var connected = await _socket.ConnectAsync(_race.Id, "committee", participantId: null);
            HasConnectionIssue = !connected;
            ConnectionStatusText = connected
                ? string.Empty
                : "Not connected to server — lap counts will update once connectivity is restored (reopen this page to retry).";

            if (connected)
            {
                await CatchUpWithServerAsync(_race.Id);
            }
        }
        catch (Exception ex)
        {
            // This method is invoked from an async-void Page.OnAppearing — an unhandled exception
            // here would crash the whole app, so nothing that can throw is allowed to escape it.
            HasConnectionIssue = true;
            ConnectionStatusText = $"Could not load the timing sheet: {ex.Message}";
        }
    }

    private void OnMessageReceived(object? sender, RaceSocketMessage message)
    {
        Ui.Dispatcher.Post(async () =>
        {
            if (message.Raw.TryGetProperty("participantId", out var pid) && pid.ValueKind == JsonValueKind.String
                && _manualAt.TryGetValue(pid.GetString()!, out var editedAt) && DateTime.UtcNow - editedAt < EchoWindow)
            {
                return;
            }

            switch (message.Type)
            {
                case "lapCompleted":
                    ApplyLapCompleted(message.Raw);
                    break;
                case "finalLap":
                    ApplyFinalLap(message.Raw);
                    break;
                case "finished":
                    ApplyFinished(message.Raw);
                    break;
                case "courseShortened":
                    ApplyCourseShortened(message.Raw);
                    break;
                case "boatTiming":
                    ApplyBoatTiming(message.Raw);
                    break;
            }

            await PersistAsync();
        });
    }

    private void ApplyLapCompleted(JsonElement raw)
    {
        var entry = FindEntry(raw);
        if (entry is null)
        {
            return;
        }

        entry.LapsCompleted = raw.TryGetProperty("lapsCompleted", out var lc) ? lc.GetInt32() : entry.LapsCompleted;
        entry.IsOnFinalLap = raw.TryGetProperty("isFinalLap", out var fl) && fl.GetBoolean();
    }

    private void ApplyFinalLap(JsonElement raw)
    {
        var entry = FindEntry(raw);
        if (entry is not null)
        {
            entry.IsOnFinalLap = true;
        }
    }

    private void ApplyFinished(JsonElement raw)
    {
        var entry = FindEntry(raw);
        if (entry is null)
        {
            return;
        }

        var wasFinished = entry.Status == RaceParticipantStatus.Finished;
        entry.Status = RaceParticipantStatus.Finished;
        entry.IsOnFinalLap = false;
        if (!wasFinished)
        {
            AutoPhoto(entry);
        }

        if (raw.TryGetProperty("finishTime", out var ft) && ft.ValueKind == JsonValueKind.String)
        {
            entry.FinishTime = DateTimeOffset.Parse(ft.GetString()!);
        }

        if (raw.TryGetProperty("elapsedSeconds", out var es))
        {
            entry.ElapsedSeconds = es.GetDouble();
        }
    }

    /// <summary>
    /// The server's full timing state for one boat — sent when a committee PC enters a lap, finish or
    /// DNF/DNS/RET/OCS. This is what keeps a second committee PC's sheet in step (and it carries corrections
    /// that undo a lap or a finish, which the lap/finished events can't express).
    /// </summary>
    private void ApplyBoatTiming(JsonElement raw)
    {
        var entry = FindEntry(raw);
        if (entry is null)
        {
            return;
        }

        if (raw.TryGetProperty("lapsCompleted", out var lc) && lc.ValueKind == JsonValueKind.Number)
        {
            entry.LapsCompleted = lc.GetInt32();
        }

        entry.IsOnFinalLap = raw.TryGetProperty("isOnFinalLap", out var fl) && fl.ValueKind == JsonValueKind.True;

        if (raw.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String
            && Enum.TryParse<RaceParticipantStatus>(st.GetString(), ignoreCase: true, out var status))
        {
            entry.Status = status;
        }

        entry.FinishTime = raw.TryGetProperty("finishTime", out var ft) && ft.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(ft.GetString()!)
            : null;
        entry.ElapsedSeconds = raw.TryGetProperty("elapsedSeconds", out var es) && es.ValueKind == JsonValueKind.Number
            ? es.GetDouble()
            : null;
    }

    private void ApplyCourseShortened(JsonElement raw)
    {
        if (!raw.TryGetProperty("raceParticipants", out var items))
        {
            return;
        }

        foreach (var item in items.EnumerateArray())
        {
            var participantId = item.GetProperty("participantId").GetString();
            var entry = Entries.FirstOrDefault(e => e.ParticipantId == participantId);
            // Laps is init-only on the display entry; the authoritative value lives server-side
            // and is re-read on next full load. Here we just mark the shorten so the committee sees it.
            if (entry is not null)
            {
                entry.IsOnFinalLap = entry.LapsCompleted >= item.GetProperty("laps").GetInt32() - 1;
            }
        }
    }

    private RaceTimingEntry? FindEntry(JsonElement raw) =>
        raw.TryGetProperty("participantId", out var idProp)
            ? Entries.FirstOrDefault(e => e.ParticipantId == idProp.GetString())
            : null;

    [RelayCommand]
    private async Task SetDnfAsync(RaceTimingEntry entry) => await SetStatusAsync(entry, RaceParticipantStatus.Dnf);

    [RelayCommand]
    private async Task SetDnsAsync(RaceTimingEntry entry) => await SetStatusAsync(entry, RaceParticipantStatus.Dns);

    [RelayCommand]
    private async Task SetRetAsync(RaceTimingEntry entry) => await SetStatusAsync(entry, RaceParticipantStatus.Ret);

    [RelayCommand]
    private async Task SetOcsAsync(RaceTimingEntry entry) => await SetStatusAsync(entry, RaceParticipantStatus.Ocs);

    private async Task SetStatusAsync(RaceTimingEntry entry, RaceParticipantStatus status)
    {
        entry.Status = status;
        await PersistAsync();
        QueueForServer(entry);
    }

    /// <summary>Manual offline fallback for lap counting: stamps "now" as this boat's next lap.</summary>
    [RelayCommand]
    private async Task RecordLapNowAsync(RaceTimingEntry entry)
    {
        // The button is disabled for a finished boat; this also stops a fast double-click on the last lap
        // from counting an extra one.
        if (!entry.CanRecordLap)
        {
            return;
        }

        entry.LapsCompleted += 1;

        var finishedNow = false;
        if (entry.LapsCompleted >= entry.Laps)
        {
            ApplyManualFinish(entry, DateTimeOffset.UtcNow);
            finishedNow = true;
        }
        else
        {
            entry.IsOnFinalLap = entry.LapsCompleted == entry.Laps - 1;
        }

        await PersistAsync();
        QueueForServer(entry);

        if (finishedNow)
        {
            AutoPhoto(entry);
        }
    }

    /// <summary>Manual offline fallback for finish detection: sets this boat's finish time from the
    /// date/time pickers, for backfilling from a stopwatch or correcting a missed GPS detection.</summary>
    [RelayCommand]
    private async Task SetFinishTimeAsync(RaceTimingEntry entry)
    {
        var local = EditFinishDate.Date + EditFinishTimeOfDay;
        var finishAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        ApplyManualFinish(entry, finishAt.ToUniversalTime());
        await PersistAsync();
        QueueForServer(entry);
    }

    [RelayCommand]
    private void ClearSelection() => SelectedEntry = null;

    private void ApplyManualFinish(RaceTimingEntry entry, DateTimeOffset finishAtUtc)
    {
        entry.Status = RaceParticipantStatus.Finished;
        entry.IsOnFinalLap = false;
        entry.LapsCompleted = Math.Max(entry.LapsCompleted, entry.Laps);
        entry.FinishTime = finishAtUtc;
        entry.ElapsedSeconds = _race?.StartAt is { } startAt ? (finishAtUtc - startAt).TotalSeconds : null;
    }

    /// <summary>The finish line's position, for the GPS stamp: the finish point if it is separate from the start
    /// line, otherwise the middle of the start/finish line.</summary>
    private static (GeoFix? Location, string Label) FinishLocationOf(RaceAggregate aggregate)
    {
        var race = aggregate.Race;
        if (!race.FinishSameAsStart && race.FinishLatitude is { } fLat && race.FinishLongitude is { } fLon)
        {
            return (new GeoFix(fLat, fLon), "finish point");
        }

        var line = aggregate.StartLine;
        if (line.CommitteeLatitude is { } cLat && line.CommitteeLongitude is { } cLon
            && line.PinLatitude is { } pLat && line.PinLongitude is { } pLon)
        {
            return (new GeoFix((cLat + pLat) / 2, (cLon + pLon) / 2), "finish line");
        }

        return (null, string.Empty);
    }

    /// <summary>Photographs a boat as it finishes — when that is switched on in Settings and it has no photo yet.</summary>
    private void AutoPhoto(RaceTimingEntry entry)
    {
        if (AppConfig.AutoFinishPhoto && !entry.HasPhoto)
        {
            _ = TakePhotoForAsync(entry, isFinish: true);
        }
    }

    /// <summary>The "Take photo" button: a photo of this boat now, stamped with the time and place.</summary>
    [RelayCommand]
    private Task TakePhotoAsync(RaceTimingEntry entry) => TakePhotoForAsync(entry, entry.IsFinished);

    private async Task TakePhotoForAsync(RaceTimingEntry entry, bool isFinish)
    {
        if (!_photoInFlight.Add(entry.ParticipantId))
        {
            return;
        }

        try
        {
            InfoMessage = $"Taking a photo of {entry.ParticipantName}…";
            var result = await _photos.TakeAsync(new FinishPhotoRequest(
                _race?.Name ?? string.Empty, entry.ParticipantName, entry.LapsCompleted, entry.Laps, isFinish,
                _raceLocation, _raceLocationLabel));

            if (result.Success)
            {
                entry.PhotoPath = result.Path;
                await PersistAsync();
            }

            InfoMessage = result.Message;
        }
        finally
        {
            _photoInFlight.Remove(entry.ParticipantId);
        }
    }

    [RelayCommand]
    private async Task OpenPhotoAsync(RaceTimingEntry entry)
    {
        if (entry.PhotoPath is { } path && File.Exists(path))
        {
            await Ui.Launcher.OpenFileAsync(path);
        }
        else
        {
            InfoMessage = "That photo file is no longer there.";
        }
    }

    [RelayCommand]
    private async Task ShortenCourseAsync()
    {
        if (_race is null)
        {
            return;
        }

        var newLaps = new List<RaceParticipantDto>();
        foreach (var entry in Entries.Where(e => e.Status == RaceParticipantStatus.Racing))
        {
            var reducedLaps = Math.Max(entry.LapsCompleted + 1, 1);
            newLaps.Add(new RaceParticipantDto { ParticipantId = entry.ParticipantId, Laps = reducedLaps });
        }

        _race.ShortenCourseAppliedAt = DateTimeOffset.UtcNow;
        await PersistAsync();

        // Fire-and-forget: the committee's local timing sheet is already updated (via PersistAsync
        // above); this must not block on the network. A down server just means competitors' web pages
        // see the shortened course next time it syncs, not that the committee action itself stalls.
        var raceId = _race.Id;
        _ = SafeCallAsync(() => _api.ShortenCourseAsync(raceId, newLaps));
    }

    /// <summary>
    /// Hands a manual entry to the sync service, which stores it in the local queue first and then sends it.
    /// Not awaited by the UI: the entry is already saved on this PC, so a slow or absent server must never
    /// hold up the committee. Whatever can't be sent now goes out when the connection returns.
    /// </summary>
    private void QueueForServer(RaceTimingEntry entry)
    {
        if (_race is null)
        {
            return;
        }

        _manualAt[entry.ParticipantId] = DateTime.UtcNow;
        var raceId = _race.Id;
        var boat = new RaceParticipant
        {
            ParticipantId = entry.ParticipantId,
            LapsCompleted = entry.LapsCompleted,
            IsOnFinalLap = entry.IsOnFinalLap,
            Status = entry.Status,
            FinishTime = entry.FinishTime,
            ElapsedSeconds = entry.ElapsedSeconds
        };
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _ = SafeCallAsync(() => _sync.QueueTimingAsync(raceId, boat, stamp));
    }

    /// <summary>True when state <paramref name="a"/> is further along than <paramref name="b"/>: finished (or
    /// otherwise out) beats still racing, and among racing boats more laps beats fewer.</summary>
    private static bool IsFurtherAlong(RaceParticipantStatus aStatus, int aLaps, RaceParticipantStatus bStatus, int bLaps)
    {
        if (aStatus != RaceParticipantStatus.Racing && bStatus == RaceParticipantStatus.Racing)
        {
            return true;
        }

        return aStatus == RaceParticipantStatus.Racing && bStatus == RaceParticipantStatus.Racing && aLaps > bLaps;
    }

    /// <summary>
    /// Brings this sheet and the server level when the page opens, without ever moving a boat backwards:
    /// where the server is further along (a GPS lap, or the other committee PC's entry) the sheet adopts it;
    /// where this PC is further along (entries made before they could be sent) they are queued for the
    /// server. Boats with an unsent entry of their own are left alone. Corrections that take a boat back —
    /// undoing a lap — travel as live events instead, or by "Take over a race" on the Race list.
    /// </summary>
    private async Task CatchUpWithServerAsync(string raceId)
    {
        try
        {
            var server = await _api.GetRaceAsync(raceId);
            if (server is null)
            {
                return;
            }

            var pending = await _sync.GetPendingTimingParticipantIdsAsync(raceId);
            var changedLocally = false;

            foreach (var remote in server.RaceParticipants)
            {
                var entry = Entries.FirstOrDefault(e => e.ParticipantId == remote.ParticipantId);
                if (entry is null || pending.Contains(remote.ParticipantId))
                {
                    continue;
                }

                var remoteStatus = Enum.TryParse<RaceParticipantStatus>(remote.Status, ignoreCase: true, out var parsed)
                    ? parsed
                    : RaceParticipantStatus.Racing;

                if (IsFurtherAlong(remoteStatus, remote.LapsCompleted, entry.Status, entry.LapsCompleted))
                {
                    entry.LapsCompleted = remote.LapsCompleted;
                    entry.IsOnFinalLap = remote.IsOnFinalLap;
                    entry.Status = remoteStatus;
                    entry.FinishTime = remote.FinishTime;
                    entry.ElapsedSeconds = remote.ElapsedSeconds;
                    changedLocally = true;
                }
                else if (IsFurtherAlong(entry.Status, entry.LapsCompleted, remoteStatus, remote.LapsCompleted))
                {
                    QueueForServer(entry);
                }
            }

            if (changedLocally)
            {
                await PersistAsync();
            }
        }
        catch
        {
            // Best effort: the live events and the outbox still do their work.
        }
    }

    private static async Task SafeCallAsync(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch
        {
            // Non-fatal: will be reflected next time the race aggregate syncs.
        }
    }

    [RelayCommand]
    private async Task ViewResultsAsync()
    {
        if (_race is null)
        {
            return;
        }

        _race.Status = RaceStatus.Finished;
        await PersistAsync();
        await Ui.Navigator.GoToAsync($"results?raceId={_race.Id}");
    }

    private async Task PersistAsync()
    {
        if (_race is null)
        {
            return;
        }

        var aggregate = await _races.GetAggregateAsync(_race.Id);
        if (aggregate is null)
        {
            return;
        }

        // Copy only what this page changes onto the freshly loaded race. Replacing it with this page's
        // own (load-time) copy would overwrite anything stored since — e.g. the join code from a sync.
        aggregate.Race.Status = _race.Status;
        aggregate.Race.ShortenCourseAppliedAt = _race.ShortenCourseAppliedAt;
        foreach (var entry in Entries)
        {
            var rp = aggregate.RaceParticipants.FirstOrDefault(x => x.ParticipantId == entry.ParticipantId);
            if (rp is null)
            {
                continue;
            }

            rp.LapsCompleted = entry.LapsCompleted;
            rp.IsOnFinalLap = entry.IsOnFinalLap;
            rp.Status = entry.Status;
            rp.FinishTime = entry.FinishTime;
            rp.ElapsedSeconds = entry.ElapsedSeconds;
            rp.FinishPhotoPath = entry.PhotoPath;
        }

        await _races.SaveAggregateAsync(aggregate);
    }

    [RelayCommand]
    private async Task OpenMapAsync()
    {
        if (MapUrl is not null)
        {
            await Ui.Launcher.OpenAsync(new Uri(MapUrl));
        }
    }

    [RelayCommand]
    private async Task OpenReplayAsync()
    {
        if (ReplayUrl is not null)
        {
            await Ui.Launcher.OpenAsync(new Uri(ReplayUrl));
        }
    }

    [RelayCommand]
    private async Task CopyMapLinkAsync()
    {
        if (MapUrl is null)
        {
            return;
        }

        await Ui.Clipboard.SetTextAsync(MapUrl);
        InfoMessage = "Copied the live map link.";
    }

    [RelayCommand]
    private async Task CopyJoinLinkAsync(RaceTimingEntry entry)
    {
        if (!entry.HasJoinUrl)
        {
            InfoMessage = "Join links appear once the race has synced to the server.";
            return;
        }

        await Ui.Clipboard.SetTextAsync(entry.JoinUrl);
        InfoMessage = $"Copied the join link for {entry.ParticipantName}.";
    }

    [RelayCommand]
    private async Task EditRaceAsync()
    {
        if (!string.IsNullOrEmpty(RaceId))
        {
            await Ui.Navigator.GoToAsync($"raceSetup?raceId={RaceId}");
        }
    }

    public void OnDisappearing()
    {
        _joinWatcher.Stop();
        _ = _photos.ReleaseAsync();
        _socket.MessageReceived -= OnMessageReceived;
        _ = _socket.DisconnectAsync();
    }

    public void Dispose() => OnDisappearing();
}

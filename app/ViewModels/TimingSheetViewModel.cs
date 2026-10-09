using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SailRacing.Data;
using SailRacing.Models;
using SailRacing.Services;

namespace SailRacing.ViewModels;

[QueryProperty(nameof(RaceId), "raceId")]
public partial class TimingSheetViewModel : BaseViewModel, IDisposable
{
    private readonly IRaceRepository _races;
    private readonly IFleetRepository _fleets;
    private readonly IParticipantRepository _participants;
    private readonly IRaceSocketClient _socket;
    private readonly ISailRacingApiClient _api;

    private readonly JoinCodeWatcher _joinWatcher;

    private Race? _race;

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
        ISailRacingApiClient api)
    {
        _races = races;
        _fleets = fleets;
        _participants = participants;
        _socket = socket;
        _api = api;
        _joinWatcher = new JoinCodeWatcher(races);
        Title = "Timing Sheet";
    }

    private void OnJoinCodeArrived(Race fresh)
    {
        foreach (var entry in Entries)
        {
            entry.JoinUrl = JoinLinks.Build(fresh.JoinCode, entry.ParticipantId);
        }

        MapUrl = JoinLinks.BuildMap(fresh.JoinCode);
        ReplayUrl = JoinLinks.BuildReplay(fresh.JoinCode);
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
            MapUrl = JoinLinks.BuildMap(_race.JoinCode);
            ReplayUrl = JoinLinks.BuildReplay(_race.JoinCode);

            var participantIds = aggregate.RaceParticipants.Select(rp => rp.ParticipantId);
            var participants = await _participants.GetByIdsAsync(participantIds);

            Entries.Clear();
            foreach (var rp in aggregate.RaceParticipants)
            {
                var participant = participants.FirstOrDefault(p => p.Id == rp.ParticipantId);
                Entries.Add(new RaceTimingEntry
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
                    JoinUrl = JoinLinks.Build(_race.JoinCode, rp.ParticipantId)
                });
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
        MainThread.BeginInvokeOnMainThread(async () =>
        {
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

        entry.Status = RaceParticipantStatus.Finished;
        entry.IsOnFinalLap = false;
        if (raw.TryGetProperty("finishTime", out var ft) && ft.ValueKind == JsonValueKind.String)
        {
            entry.FinishTime = DateTimeOffset.Parse(ft.GetString()!);
        }

        if (raw.TryGetProperty("elapsedSeconds", out var es))
        {
            entry.ElapsedSeconds = es.GetDouble();
        }
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
    }

    /// <summary>Manual offline fallback for lap counting: stamps "now" as this boat's next lap.</summary>
    [RelayCommand]
    private async Task RecordLapNowAsync(RaceTimingEntry entry)
    {
        entry.LapsCompleted += 1;

        if (entry.LapsCompleted >= entry.Laps)
        {
            ApplyManualFinish(entry, DateTimeOffset.UtcNow);
        }
        else
        {
            entry.IsOnFinalLap = entry.LapsCompleted == entry.Laps - 1;
        }

        await PersistAsync();
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
        await Shell.Current.GoToAsync($"results?raceId={_race.Id}");
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
        }

        await _races.SaveAggregateAsync(aggregate);
    }

    [RelayCommand]
    private async Task OpenMapAsync()
    {
        if (MapUrl is not null)
        {
            await Launcher.Default.OpenAsync(new Uri(MapUrl));
        }
    }

    [RelayCommand]
    private async Task OpenReplayAsync()
    {
        if (ReplayUrl is not null)
        {
            await Launcher.Default.OpenAsync(new Uri(ReplayUrl));
        }
    }

    [RelayCommand]
    private async Task CopyMapLinkAsync()
    {
        if (MapUrl is null)
        {
            return;
        }

        await Clipboard.Default.SetTextAsync(MapUrl);
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

        await Clipboard.Default.SetTextAsync(entry.JoinUrl);
        InfoMessage = $"Copied the join link for {entry.ParticipantName}.";
    }

    [RelayCommand]
    private async Task EditRaceAsync()
    {
        if (!string.IsNullOrEmpty(RaceId))
        {
            await Shell.Current.GoToAsync($"raceSetup?raceId={RaceId}");
        }
    }

    public void OnDisappearing()
    {
        _joinWatcher.Stop();
        _socket.MessageReceived -= OnMessageReceived;
        _ = _socket.DisconnectAsync();
    }

    public void Dispose() => OnDisappearing();
}

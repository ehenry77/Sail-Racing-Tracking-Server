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

    private Race? _race;

    public string? RaceId { get; set; }

    public ObservableCollection<RaceTimingEntry> Entries { get; } = new();

    [ObservableProperty]
    private string connectionStatusText = string.Empty;

    [ObservableProperty]
    private bool hasConnectionIssue;

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
        Title = "Timing Sheet";
        _socket.MessageReceived += OnMessageReceived;
    }

    public async Task OnAppearingAsync()
    {
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
                    ElapsedSeconds = rp.ElapsedSeconds
                });
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

        try
        {
            await _api.ShortenCourseAsync(_race.Id, newLaps);
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

        aggregate.Race = _race;
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

    public void Dispose()
    {
        _socket.MessageReceived -= OnMessageReceived;
        _ = _socket.DisconnectAsync();
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SailRacing.Data;
using SailRacing.Models;
using SailRacing.Services;

namespace SailRacing.ViewModels;

[QueryProperty(nameof(RaceId), "raceId")]
public partial class StartSequenceViewModel : BaseViewModel, IDisposable
{
    private readonly IRaceRepository _races;
    private readonly IParticipantRepository _participants;
    private readonly IStartSequenceService _sequence;
    private readonly ISailRacingApiClient _api;
    private readonly JoinCodeWatcher _joinWatcher;

    private Race? _race;
    private IDispatcherTimer? _preStartTimer;

    public string? RaceId { get; set; }

    public ObservableCollection<BoatLinkItem> BoatLinks { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMapUrl))]
    [NotifyPropertyChangedFor(nameof(MapLinkText))]
    private string? mapUrl;

    public bool HasMapUrl => !string.IsNullOrEmpty(MapUrl);

    public string MapLinkText => MapUrl is null ? JoinLinks.MapNotSyncedText : $"Live map: {MapUrl}";

    [ObservableProperty]
    private string linkMessage = string.Empty;

    [ObservableProperty]
    private bool classFlagUp;

    [ObservableProperty]
    private bool pFlagUp;

    [ObservableProperty]
    private string countdownText = "--:--";

    [ObservableProperty]
    private bool isSequenceComplete;

    [ObservableProperty]
    private bool isSequenceStarted;

    [ObservableProperty]
    private string startAtText = string.Empty;

    /// <summary>Live clock + scheduled-start countdown shown before "Begin Start Sequence" is pressed.</summary>
    [ObservableProperty]
    private string currentTimeText = string.Empty;

    [ObservableProperty]
    private string scheduledCountdownText = string.Empty;

    [ObservableProperty]
    private bool hasScheduledStartTime;

    public StartSequenceViewModel(
        IRaceRepository races,
        IParticipantRepository participants,
        IStartSequenceService sequence,
        ISailRacingApiClient api)
    {
        _races = races;
        _participants = participants;
        _sequence = sequence;
        _api = api;
        _joinWatcher = new JoinCodeWatcher(races);
        Title = "Start Sequence";
    }

    public async Task OnAppearingAsync()
    {
        // Subscribe per appearance (not in the constructor) and drop it in OnDisappearing: the user can
        // now leave this page mid-sequence and come back, and the sequence service is a singleton that
        // keeps running — a stale view model must not stay subscribed to it.
        _sequence.StatusChanged -= OnStatusChanged;
        _sequence.StatusChanged += OnStatusChanged;

        if (string.IsNullOrEmpty(RaceId))
        {
            return;
        }

        var aggregate = await _races.GetAggregateAsync(RaceId);
        _race = aggregate?.Race;
        await LoadLinksAsync(aggregate);

        if (_race?.StartAt is not null)
        {
            IsSequenceStarted = true;
            StartAtText = FormatStartAt(_race.StartAt.Value);
            _sequence.Start(_race.StartAt.Value);
        }
        else
        {
            HasScheduledStartTime = _race?.ScheduledStartTime is not null;
            StartPreStartTimer();
        }
    }

    private async Task LoadLinksAsync(RaceAggregate? aggregate)
    {
        BoatLinks.Clear();
        if (aggregate is null)
        {
            return;
        }

        var participants = await _participants.GetByIdsAsync(aggregate.RaceParticipants.Select(rp => rp.ParticipantId));
        foreach (var rp in aggregate.RaceParticipants)
        {
            BoatLinks.Add(new BoatLinkItem
            {
                ParticipantId = rp.ParticipantId,
                ParticipantName = participants.FirstOrDefault(p => p.Id == rp.ParticipantId)?.Name ?? "(unknown)",
                JoinUrl = JoinLinks.Build(aggregate.Race.JoinCode, rp.ParticipantId)
            });
        }

        MapUrl = JoinLinks.BuildMap(aggregate.Race.JoinCode);

        // The server assigns the join code during the background sync from Race Setup, which often lands a
        // moment after this page loads — pick it up when it does rather than showing "not synced" until
        // the next visit.
        if (string.IsNullOrEmpty(aggregate.Race.JoinCode))
        {
            _joinWatcher.Start(aggregate.Race.Id, OnJoinCodeArrived);
        }
    }

    private void OnJoinCodeArrived(Race fresh)
    {
        foreach (var item in BoatLinks)
        {
            item.JoinUrl = JoinLinks.Build(fresh.JoinCode, item.ParticipantId);
        }

        MapUrl = JoinLinks.BuildMap(fresh.JoinCode);
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
    private async Task CopyMapLinkAsync()
    {
        if (MapUrl is null)
        {
            return;
        }

        await Clipboard.Default.SetTextAsync(MapUrl);
        LinkMessage = "Copied the live map link.";
    }

    [RelayCommand]
    private async Task CopyBoatLinkAsync(BoatLinkItem item)
    {
        if (!item.HasJoinUrl)
        {
            LinkMessage = "Join links appear once the race has synced to the server.";
            return;
        }

        await Clipboard.Default.SetTextAsync(item.JoinUrl);
        LinkMessage = $"Copied the join link for {item.ParticipantName}.";
    }

    private void StartPreStartTimer()
    {
        StopPreStartTimer();

        _preStartTimer = Application.Current!.Dispatcher.CreateTimer();
        _preStartTimer.Interval = TimeSpan.FromSeconds(1);
        _preStartTimer.Tick += (_, _) => UpdatePreStartDisplay();
        _preStartTimer.Start();

        UpdatePreStartDisplay();
    }

    private void UpdatePreStartDisplay()
    {
        var now = DateTimeOffset.Now;
        CurrentTimeText = now.ToString("HH:mm:ss");

        if (_race?.ScheduledStartTime is { } scheduled)
        {
            var remaining = scheduled - DateTimeOffset.UtcNow;
            ScheduledCountdownText = remaining > TimeSpan.Zero
                ? $"Scheduled start in {FormatDuration(remaining)}"
                : "Scheduled start time has passed";
        }
    }

    private static string FormatDuration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t:mm\\:ss}" : $"{t:mm\\:ss}";

    private void StopPreStartTimer()
    {
        if (_preStartTimer is not null)
        {
            _preStartTimer.Stop();
            _preStartTimer = null;
        }
    }

    [RelayCommand]
    private async Task BeginSequenceAsync()
    {
        if (_race is null)
        {
            return;
        }

        // Use the scheduled start time if one was set during setup and it's still ahead of us;
        // otherwise start the sequence right now, which puts the gun one full lead time (the earliest
        // alarm or signal in Settings — 10 minutes with the standard sequence) from now.
        var now = DateTimeOffset.UtcNow;
        var startAt = _race.ScheduledStartTime is { } scheduled && scheduled > now
            ? scheduled
            : now + AppConfig.LoadSequenceSettings().LeadTime;
        var raceId = _race.Id;

        await SaveOwnedFieldsAsync(r =>
        {
            r.StartAt = startAt;
            r.Status = RaceStatus.StartSequence;
        });

        StopPreStartTimer();
        IsSequenceStarted = true;
        StartAtText = FormatStartAt(startAt);
        _sequence.Start(startAt);

        // Fire-and-forget: the sequence itself runs entirely off the locally-persisted StartAt and
        // must never wait on the network — a down server just means it catches up next time the
        // race syncs (RaceSyncService's connectivity watcher retries automatically).
        _ = SafeCallAsync(() => _api.StartSequenceAsync(raceId, startAt));
    }

    /// <summary>
    /// Saves only the fields this page owns onto a freshly loaded race. Writing back this page's own
    /// copy of the race (loaded when the page opened) would overwrite whatever happened since — most
    /// importantly the join code, which the background sync from Race Setup usually stores a moment
    /// after this page loads. That wiped the join links the moment the race was launched.
    /// </summary>
    private async Task SaveOwnedFieldsAsync(Action<Race> apply)
    {
        var aggregate = await _races.GetAggregateAsync(_race!.Id);
        if (aggregate is null)
        {
            return;
        }

        apply(aggregate.Race);
        await _races.SaveAggregateAsync(aggregate);
        _race = aggregate.Race;
    }

    [RelayCommand]
    private async Task AllClearAsync()
    {
        if (_race is null)
        {
            return;
        }

        var raceId = _race.Id;
        await SaveOwnedFieldsAsync(r => r.Status = RaceStatus.Racing);

        // Fire-and-forget: the committee must be able to move into Race Mode immediately regardless
        // of server reachability — waiting here would block navigation for up to the HTTP timeout.
        _ = SafeCallAsync(() => _api.AllClearAsync(raceId));

        await Shell.Current.GoToAsync($"timingSheet?raceId={raceId}");
    }

    private static async Task SafeCallAsync(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch
        {
            // Non-fatal: retried implicitly next time the race is pushed/synced.
        }
    }

    private void OnStatusChanged(object? sender, StartSequenceStatus status)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ClassFlagUp = status.ClassFlagUp;
            PFlagUp = status.PFlagUp;
            IsSequenceComplete = status.IsComplete;

            var t = status.TimeToStart;
            CountdownText = t >= TimeSpan.Zero
                ? $"-{t:mm\\:ss}"
                : $"+{t.Negate():mm\\:ss}";
        });
    }

    private static string FormatStartAt(DateTimeOffset startAt) => $"Start signal at {startAt.ToLocalTime():HH:mm:ss}";

    [RelayCommand]
    private async Task EditRaceAsync()
    {
        if (!string.IsNullOrEmpty(RaceId))
        {
            await Shell.Current.GoToAsync($"raceSetup?raceId={RaceId}");
        }
    }

    /// <summary>Leaving the page must not stop the sequence itself — it keeps counting down and
    /// announcing in the background; only this view model's UI hooks are released.</summary>
    public void OnDisappearing()
    {
        _sequence.StatusChanged -= OnStatusChanged;
        StopPreStartTimer();
        _joinWatcher.Stop();
    }

    public void Dispose() => OnDisappearing();
}

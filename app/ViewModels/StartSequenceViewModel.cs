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
    private readonly IStartSequenceService _sequence;
    private readonly ISailRacingApiClient _api;

    private Race? _race;
    private IDispatcherTimer? _preStartTimer;

    public string? RaceId { get; set; }

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

    public StartSequenceViewModel(IRaceRepository races, IStartSequenceService sequence, ISailRacingApiClient api)
    {
        _races = races;
        _sequence = sequence;
        _api = api;
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
        // otherwise the standard "gun in 10 minutes from right now" sequence.
        var now = DateTimeOffset.UtcNow;
        _race.StartAt = _race.ScheduledStartTime is { } scheduled && scheduled > now
            ? scheduled
            : now.AddMinutes(10);
        _race.Status = RaceStatus.StartSequence;

        var aggregate = await _races.GetAggregateAsync(_race.Id);
        if (aggregate is not null)
        {
            aggregate.Race = _race;
            await _races.SaveAggregateAsync(aggregate);
        }

        StopPreStartTimer();
        IsSequenceStarted = true;
        StartAtText = FormatStartAt(_race.StartAt.Value);
        _sequence.Start(_race.StartAt.Value);

        // Fire-and-forget: the sequence itself runs entirely off the locally-persisted StartAt and
        // must never wait on the network — a down server just means it catches up next time the
        // race syncs (RaceSyncService's connectivity watcher retries automatically).
        _ = SafeCallAsync(() => _api.StartSequenceAsync(_race.Id, _race.StartAt.Value));
    }

    [RelayCommand]
    private async Task AllClearAsync()
    {
        if (_race is null)
        {
            return;
        }

        _race.Status = RaceStatus.Racing;
        var aggregate = await _races.GetAggregateAsync(_race.Id);
        if (aggregate is not null)
        {
            aggregate.Race = _race;
            await _races.SaveAggregateAsync(aggregate);
        }

        // Fire-and-forget: the committee must be able to move into Race Mode immediately regardless
        // of server reachability — waiting here would block navigation for up to the HTTP timeout.
        _ = SafeCallAsync(() => _api.AllClearAsync(_race.Id));

        await Shell.Current.GoToAsync($"timingSheet?raceId={_race.Id}");
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
    }

    public void Dispose() => OnDisappearing();
}

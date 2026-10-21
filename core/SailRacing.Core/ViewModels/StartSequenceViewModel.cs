using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SailRacing.Data;
using SailRacing.Models;
using SailRacing.Services;

namespace SailRacing.ViewModels;

public partial class StartSequenceViewModel : RaceViewModelBase, IHasRaceId, IDisposable
{
    private readonly IRaceRepository _races;
    private readonly IParticipantRepository _participants;
    private readonly IStartSequenceService _sequence;
    private readonly ISailRacingApiClient _api;
    private readonly IRaceSyncService _sync;
    private readonly IStartVideoService _video;
    private readonly JoinCodeWatcher _joinWatcher;

    private Race? _race;

    // Where the race is, for the GPS stamp on the video when this PC has no location of its own.
    private GeoFix? _raceLocation;
    private string _raceLocationLabel = string.Empty;
    private IUiTimer? _preStartTimer;
    private IUiTimer? _videoTimer;

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

    // ----- the video of the start: recorded from the last minute before the start until All Clear -----

    /// <summary>This race's video is being recorded right now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VideoButtonText))]
    private bool isRecordingVideo;

    /// <summary>This race's video is waiting for the last minute to begin.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VideoButtonText))]
    private bool isVideoScheduled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVideoStatus))]
    private string videoStatusText = string.Empty;

    /// <summary>The finished video of this race's start, if there is one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStartVideo))]
    private string? startVideoPath;

    public bool HasVideoStatus => !string.IsNullOrEmpty(VideoStatusText);

    public bool HasStartVideo => !string.IsNullOrEmpty(StartVideoPath);

    public string VideoButtonText => IsRecordingVideo ? "Stop recording" : IsVideoScheduled ? "Cancel the video" : "Record video now";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancelSequence))]
    private bool isSequenceStarted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancelSequence))]
    private bool isRaceRunning;

    /// <summary>The sequence can be cancelled until All Clear — after that the race itself is under way.</summary>
    public bool CanCancelSequence => IsSequenceStarted && !IsRaceRunning;

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
        ISailRacingApiClient api,
        IRaceSyncService sync,
        IStartVideoService video,
        IUiServices ui) : base(ui)
    {
        _video = video;
        _races = races;
        _participants = participants;
        _sequence = sequence;
        _api = api;
        _sync = sync;
        _joinWatcher = new JoinCodeWatcher(races, ui.Dispatcher);
        Title = "Start Sequence";
    }

    public async Task OnAppearingAsync()
    {
        // Subscribe per appearance (not in the constructor) and drop it in OnDisappearing: the user can
        // now leave this page mid-sequence and come back, and the sequence service is a singleton that
        // keeps running — a stale view model must not stay subscribed to it.
        _sequence.StatusChanged -= OnStatusChanged;
        _sequence.StatusChanged += OnStatusChanged;
        _video.StateChanged -= OnVideoStateChanged;
        _video.StateChanged += OnVideoStateChanged;

        if (string.IsNullOrEmpty(RaceId))
        {
            return;
        }

        var aggregate = await _races.GetAggregateAsync(RaceId);
        _race = aggregate?.Race;
        if (aggregate is not null)
        {
            (_raceLocation, _raceLocationLabel) = RaceLocation.Of(aggregate);
        }

        IsRaceRunning = _race?.Status is RaceStatus.Racing or RaceStatus.Finished;
        await LoadLinksAsync(aggregate);
        StartVideoPath = _race?.StartVideoPath;

        if (_race?.StartAt is not null)
        {
            IsSequenceStarted = true;
            StartAtText = FormatStartAt(_race.StartAt.Value);
            _sequence.Start(_race.StartAt.Value, _race.Id);

            // Back on this screen (or the app was restarted) with the sequence still ahead of the start: make sure the
            // video is lined up for the last minute. If it is already recording or waiting, nothing changes.
            if (AppConfig.RecordStartVideo
                && _race.Status == RaceStatus.StartSequence
                && _race.StartAt.Value > DateTimeOffset.UtcNow
                && !_video.IsRecording && !_video.IsScheduled)
            {
                _video.Schedule(_race.Id, _race.Name, _race.StartAt.Value, _raceLocation, _raceLocationLabel);
            }
        }
        else
        {
            HasScheduledStartTime = _race?.ScheduledStartTime is not null;
            StartPreStartTimer();
        }

        RefreshVideoState();
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
        ShowRace(aggregate.Race.Name, aggregate.Race.JoinCode);

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
        ShowRace(fresh.Name, fresh.JoinCode);
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
    private async Task CopyMapLinkAsync()
    {
        if (MapUrl is null)
        {
            return;
        }

        await Ui.Clipboard.SetTextAsync(MapUrl);
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

        await Ui.Clipboard.SetTextAsync(item.JoinUrl);
        LinkMessage = $"Copied the join link for {item.ParticipantName}.";
    }

    private void StartPreStartTimer()
    {
        StopPreStartTimer();

        _preStartTimer = Ui.Dispatcher.CreateTimer();
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
        _sequence.Start(startAt, raceId);

        // The video of the start: switched on shortly before the last minute, and recording until All Clear.
        if (AppConfig.RecordStartVideo)
        {
            _video.Schedule(raceId, _race.Name, startAt, _raceLocation, _raceLocationLabel);
        }

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

        // The video ends at the moment of All Clear. Finishing the file takes a moment, so don't wait for it.
        _ = _video.StopAsync();

        await SaveOwnedFieldsAsync(r => r.Status = RaceStatus.Racing);

        // Fire-and-forget: the committee must be able to move into Race Mode immediately regardless
        // of server reachability — waiting here would block navigation for up to the HTTP timeout.
        _ = SafeCallAsync(() => _api.AllClearAsync(raceId));

        await Ui.Navigator.GoToAsync($"timingSheet?raceId={raceId}");
    }

    /// <summary>
    /// Abandons the running start sequence and returns to the pre-start screen, ready to begin again.
    /// Stops the timeline and announcements, puts the flags down, clears the race's start time locally
    /// and then on the server (through the normal sync, so an offline venue just retries later).
    /// </summary>
    [RelayCommand]
    private async Task CancelSequenceAsync()
    {
        if (_race is null || !CanCancelSequence)
        {
            return;
        }

        var confirmed = await Ui.Dialogs.ConfirmAsync(
            "Cancel the start sequence?",
            "The countdown stops, the flags come down and the race goes back to before the start. " +
            "Positions recorded during this sequence are discarded. You can begin a new sequence afterwards.",
            "Cancel sequence", "Keep running");
        if (!confirmed)
        {
            return;
        }

        _sequence.Cancel();

        // A video that was waiting for the last minute is dropped; one already recording is stopped and kept.
        _ = _video.StopAsync();

        var raceId = _race.Id;
        await SaveOwnedFieldsAsync(r =>
        {
            r.StartAt = null;
            r.Status = RaceStatus.Setup;
        });

        ClassFlagUp = false;
        PFlagUp = false;
        IsSequenceComplete = false;
        IsSequenceStarted = false;
        CountdownText = "--:--";
        StartAtText = string.Empty;
        HasScheduledStartTime = _race?.ScheduledStartTime is not null;
        StartPreStartTimer();

        // Fire-and-forget like every other server call: the cancel is already effective locally, and
        // PushAsync marks the race for retry if the server can't be reached right now.
        _ = SafeCallAsync(() => _sync.PushAsync(raceId));
    }

    /// <summary>The manual button: start recording now, or stop (or cancel the waiting) recording.</summary>
    [RelayCommand]
    private async Task ToggleStartVideoAsync()
    {
        if (_race is null)
        {
            return;
        }

        if (_video.IsRecording || _video.IsScheduled)
        {
            await _video.StopAsync();
        }
        else
        {
            await _video.StartAsync(_race.Id, _race.Name, _raceLocation, _raceLocationLabel);
        }
    }

    [RelayCommand]
    private async Task OpenStartVideoAsync()
    {
        if (StartVideoPath is { } path && File.Exists(path))
        {
            await Ui.Launcher.OpenFileAsync(path);
        }
        else
        {
            VideoStatusText = "That video file is no longer there.";
        }
    }

    private void OnVideoStateChanged(object? sender, EventArgs e) => Ui.Dispatcher.Post(RefreshVideoState);

    /// <summary>Brings the video line and buttons in step with the service (and ticks the recording clock).</summary>
    private void RefreshVideoState()
    {
        var raceId = RaceId;
        IsRecordingVideo = raceId is not null && _video.IsRecording && _video.RaceId == raceId;
        IsVideoScheduled = raceId is not null && _video.IsScheduled && _video.ScheduledRaceId == raceId;

        if (raceId is not null && _video.LastRaceId == raceId && !string.IsNullOrEmpty(_video.LastFile))
        {
            StartVideoPath = _video.LastFile;
        }

        if (IsRecordingVideo)
        {
            var elapsed = DateTimeOffset.Now - (_video.StartedAt ?? DateTimeOffset.Now);
            VideoStatusText = $"● Recording the start  {(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}" + (_video.HasSound ? "" : "  (no sound)") + (_video.HasOverlay ? "" : "  (time and GPS in the subtitle file only)");
        }
        else if (IsVideoScheduled && _video.RecordingStartsAt is { } startsAt)
        {
            VideoStatusText = $"Video: recording starts at {startsAt.ToLocalTime():HH:mm:ss}, one minute before the start";
        }
        else if (raceId is not null && _video.MessageRaceId == raceId)
        {
            VideoStatusText = _video.LastMessage;
        }
        else
        {
            VideoStatusText = string.Empty;
        }

        // A clock only needs ticking while a video is recording or waiting.
        if ((IsRecordingVideo || IsVideoScheduled) && _videoTimer is null)
        {
            _videoTimer = Ui.Dispatcher.CreateTimer();
            _videoTimer.Interval = TimeSpan.FromSeconds(1);
            _videoTimer.Tick += (_, _) => RefreshVideoState();
            _videoTimer.Start();
        }
        else if (!IsRecordingVideo && !IsVideoScheduled && _videoTimer is not null)
        {
            _videoTimer.Stop();
            _videoTimer = null;
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
            // Non-fatal: retried implicitly next time the race is pushed/synced.
        }
    }

    private void OnStatusChanged(object? sender, StartSequenceStatus status)
    {
        Ui.Dispatcher.Post(() =>
        {
            ClassFlagUp = status.ClassFlagUp;
            PFlagUp = status.PFlagUp;
            IsSequenceComplete = status.IsComplete;

            // Whole seconds from the service, already rounded so the digits match the spoken countdown.
            var t = status.TimeToStart;
            CountdownText = t > TimeSpan.Zero
                ? $"-{FormatCountdown(t)}"
                : t < TimeSpan.Zero ? $"+{FormatCountdown(t.Negate())}" : FormatCountdown(t);
        });
    }

    // Minutes can exceed 59 for a long lead-in, so count total minutes rather than wrapping at the hour.
    private static string FormatCountdown(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";

    private static string FormatStartAt(DateTimeOffset startAt) => $"Start signal at {startAt.ToLocalTime():HH:mm:ss}";

    [RelayCommand]
    private async Task EditRaceAsync()
    {
        if (!string.IsNullOrEmpty(RaceId))
        {
            await Ui.Navigator.GoToAsync($"raceSetup?raceId={RaceId}");
        }
    }

    /// <summary>Leaving the page must not stop the sequence itself — it keeps counting down and
    /// announcing in the background; only this view model's UI hooks are released.</summary>
    public void OnDisappearing()
    {
        _sequence.StatusChanged -= OnStatusChanged;
        _video.StateChanged -= OnVideoStateChanged;
        _videoTimer?.Stop();
        _videoTimer = null;
        StopPreStartTimer();
        _joinWatcher.Stop();
    }

    public void Dispose() => OnDisappearing();
}

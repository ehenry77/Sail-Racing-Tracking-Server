namespace SailRacing.Services;

/// <summary>
/// Drives the RRS-style start sequence off a single 1-second dispatcher timer and a precomputed,
/// sorted timeline. A cursor index (not a fired-flags set) tracks progress, so a delayed tick fires
/// every event it missed and self-corrects instead of drifting or double-firing.
/// </summary>
public class StartSequenceService : IStartSequenceService, IDisposable
{
    // Built from the committee's settings each time a sequence starts, so a change in Settings applies to
    // the next race without restarting the app.
    private IReadOnlyList<SequenceEvent> _timeline = Array.Empty<SequenceEvent>();
    private readonly SemaphoreSlim _speechLock = new(1, 1);

    private IDispatcherTimer? _timer;
    private DateTimeOffset _startAt;
    private int _cursor;
    private bool _classFlagUp;
    private bool _pFlagUp;
    private CancellationTokenSource? _tickCts;
    private CancellationTokenSource _sessionCts = new();

    public bool IsRunning { get; private set; }

    public event EventHandler<StartSequenceStatus>? StatusChanged;

    public void Start(DateTimeOffset startAt)
    {
        Stop();
        _tickCts?.Cancel();
        _tickCts = null;
        _sessionCts.Cancel();
        _sessionCts = new CancellationTokenSource();

        _timeline = StartSequenceTimeline.Build(AppConfig.LoadSequenceSettings());
        _startAt = startAt;
        _cursor = 0;
        _classFlagUp = false;
        _pFlagUp = false;

        // Silently fast-forward past anything already in the past (e.g. the app was restarted
        // mid-sequence) so flag state is correct without re-speaking stale countdown numbers. The
        // small buffer avoids a race where an event due "right now" gets swallowed here instead of
        // firing (with its announcement spoken) on the very next real tick.
        var elapsedNow = DateTimeOffset.UtcNow - _startAt - TimeSpan.FromMilliseconds(500);
        while (_cursor < _timeline.Count && _timeline[_cursor].Offset < elapsedNow)
        {
            ApplyFlagState(_timeline[_cursor]);
            _cursor++;
        }

        IsRunning = true;

        _timer = Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += OnTick;
        _timer.Start();

        RaiseStatus();
    }

    public void Stop()
    {
        if (_timer is not null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }

        IsRunning = false;
    }

    public void Cancel()
    {
        Stop();
        _tickCts?.Cancel();
        _tickCts = null;
        _sessionCts.Cancel();
        _sessionCts = new CancellationTokenSource();

        _timeline = Array.Empty<SequenceEvent>();
        _cursor = 0;
        _classFlagUp = false;
        _pFlagUp = false;
        RaiseStatus(isComplete: false, timeToStart: TimeSpan.Zero);

        _ = SpeakNowAsync("Start sequence cancelled", CancellationToken.None);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var elapsed = DateTimeOffset.UtcNow - _startAt;

        while (_cursor < _timeline.Count && _timeline[_cursor].Offset <= elapsed)
        {
            var evt = _timeline[_cursor];
            ApplyFlagState(evt);
            _ = SpeakAsync(evt);
            _cursor++;
        }

        RaiseStatus();

        if (_cursor >= _timeline.Count)
        {
            Stop();
        }
    }

    private void ApplyFlagState(SequenceEvent evt)
    {
        switch (evt.Type)
        {
            case SequenceEventType.ClassFlagUp:
                _classFlagUp = true;
                break;
            case SequenceEventType.PFlagUp:
                _pFlagUp = true;
                break;
            case SequenceEventType.PFlagDown:
                _pFlagUp = false;
                break;
            case SequenceEventType.ClassFlagDown:
                _classFlagUp = false;
                break;
        }
    }

    /// <summary>
    /// Countdown digits ("10"..."1") are low priority: if speech is falling behind the 1-second
    /// cadence, a newer digit cancels whatever digit is still speaking/queued rather than piling up
    /// an ever-growing backlog. Flag/alarm announcements are high priority: they always cut off a
    /// lagging digit and speak immediately, so they're never silently delayed behind a queue — which
    /// is what made the -5:00/-4:00/-1:00/0:00 calls hard to hear before this fix.
    /// </summary>
    private async Task SpeakAsync(SequenceEvent evt)
    {
        var text = evt.Text;

        if (evt.Type == SequenceEventType.CountdownTick)
        {
            _tickCts?.Cancel();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_sessionCts.Token);
            _tickCts = cts;
            await SpeakNowAsync(text, cts.Token);
        }
        else
        {
            _tickCts?.Cancel();
            await SpeakNowAsync(text, _sessionCts.Token);
        }
    }

    private async Task SpeakNowAsync(string text, CancellationToken token)
    {
        try
        {
            await _speechLock.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return; // superseded before we even got to speak it — nothing was acquired, nothing to release
        }

        try
        {
            await TextToSpeech.Default.SpeakAsync(text, cancelToken: token);
        }
        catch (OperationCanceledException)
        {
            // Expected: superseded by a newer countdown digit or a higher-priority flag/alarm call.
        }
        catch
        {
            // Best-effort: a TTS failure (e.g. no engine available on this platform) must never
            // interrupt the timer driving the actual flag sequence.
        }
        finally
        {
            _speechLock.Release();
        }
    }

    private void RaiseStatus(bool? isComplete = null, TimeSpan? timeToStart = null)
    {
        var elapsed = DateTimeOffset.UtcNow - _startAt;

        StatusChanged?.Invoke(this, new StartSequenceStatus
        {
            ClassFlagUp = _classFlagUp,
            PFlagUp = _pFlagUp,
            TimeToStart = timeToStart ?? -elapsed,
            IsComplete = isComplete ?? _cursor >= _timeline.Count
        });
    }

    public void Dispose()
    {
        Stop();
        _tickCts?.Cancel();
        _tickCts = null;
        _sessionCts.Cancel();
    }
}

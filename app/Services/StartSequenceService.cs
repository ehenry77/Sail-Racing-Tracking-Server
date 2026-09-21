namespace SailRacing.Services;

/// <summary>
/// Drives the RRS-style start sequence off a single 1-second dispatcher timer and a precomputed,
/// sorted timeline. A cursor index (not a fired-flags set) tracks progress, so a delayed tick fires
/// every event it missed and self-corrects instead of drifting or double-firing.
/// </summary>
public class StartSequenceService : IStartSequenceService, IDisposable
{
    private readonly IReadOnlyList<SequenceEvent> _timeline = StartSequenceTimeline.Build();
    private readonly SemaphoreSlim _speechLock = new(1, 1);

    private IDispatcherTimer? _timer;
    private DateTimeOffset _startAt;
    private int _cursor;
    private bool _classFlagUp;
    private bool _pFlagUp;

    public bool IsRunning { get; private set; }

    public event EventHandler<StartSequenceStatus>? StatusChanged;

    public void Start(DateTimeOffset startAt)
    {
        Stop();

        _startAt = startAt;
        _cursor = 0;
        _classFlagUp = false;
        _pFlagUp = false;

        // Silently fast-forward past anything already in the past (e.g. the app was restarted
        // mid-sequence) so flag state is correct without re-speaking stale countdown numbers.
        var elapsedNow = DateTimeOffset.UtcNow - _startAt;
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

    private async Task SpeakAsync(SequenceEvent evt)
    {
        var text = StartSequenceTimeline.SpokenText(evt);
        await _speechLock.WaitAsync();
        try
        {
            await TextToSpeech.Default.SpeakAsync(text);
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

    private void RaiseStatus()
    {
        var elapsed = DateTimeOffset.UtcNow - _startAt;

        StatusChanged?.Invoke(this, new StartSequenceStatus
        {
            ClassFlagUp = _classFlagUp,
            PFlagUp = _pFlagUp,
            TimeToStart = -elapsed,
            IsComplete = _cursor >= _timeline.Count
        });
    }

    public void Dispose() => Stop();
}

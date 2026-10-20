namespace SailRacing.Services;

public class StartSequenceStatus
{
    public bool ClassFlagUp { get; init; }

    public bool PFlagUp { get; init; }

    public TimeSpan TimeToStart { get; init; }

    public bool IsComplete { get; init; }
}

public interface IStartSequenceService
{
    bool IsRunning { get; }

    /// <summary>The race whose sequence was last started (null if started without one).</summary>
    string? RaceId { get; }

    event EventHandler<StartSequenceStatus>? StatusChanged;

    /// <summary>Starts (or resumes, after an app restart) the sequence timeline anchored to the given T0.</summary>
    void Start(DateTimeOffset startAt, string? raceId = null);

    void Stop();

    /// <summary>
    /// Abandons the sequence: stops the timeline, silences any announcement in progress, puts both flags
    /// down and says "Start sequence cancelled". The next <see cref="Start"/> begins a fresh sequence.
    /// </summary>
    void Cancel();
}

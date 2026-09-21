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

    event EventHandler<StartSequenceStatus>? StatusChanged;

    /// <summary>Starts (or resumes, after an app restart) the sequence timeline anchored to the given T0.</summary>
    void Start(DateTimeOffset startAt);

    void Stop();
}

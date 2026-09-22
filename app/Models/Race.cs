using SQLite;

namespace SailRacing.Models;

public enum RaceStatus
{
    Setup,
    StartSequence,
    Racing,
    Finished
}

public enum SyncStatus
{
    NotSynced,
    Synced,
    Failed
}

public class Race
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    [Indexed]
    public string FleetId { get; set; } = string.Empty;

    public RaceStatus Status { get; set; } = RaceStatus.Setup;

    public int LapsDefault { get; set; } = 3;

    /// <summary>
    /// Planned wall-clock time for the start signal, set during race setup. When present and still
    /// in the future, "Begin Start Sequence" uses it as StartAt; otherwise the sequence starts
    /// 10 minutes from whenever the button is pressed.
    /// </summary>
    public DateTimeOffset? ScheduledStartTime { get; set; }

    /// <summary>
    /// T0 of the start sequence (the actual Start signal instant). Authoritative baseline for all
    /// elapsed-time math; the All Clear button only gates the UI into race mode, it never changes this.
    /// </summary>
    public DateTimeOffset? StartAt { get; set; }

    public DateTimeOffset? ShortenCourseAppliedAt { get; set; }

    public bool FinishSameAsStart { get; set; } = true;

    public double? FinishLatitude { get; set; }

    public double? FinishLongitude { get; set; }

    public string? RemoteRaceId { get; set; }

    public string? JoinCode { get; set; }

    public SyncStatus SyncStatus { get; set; } = SyncStatus.NotSynced;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

using SQLite;

namespace SailRacing.Models;

/// <summary>
/// A manual Timing Sheet entry (lap, finish, DNF...) that hasn't reached the server yet. One row per boat
/// holding its latest state — a newer entry replaces an unsent older one, since the server only needs the
/// final state of each boat and applies the newest write.
/// </summary>
public class PendingTimingUpdate
{
    /// <summary>"raceId:participantId".</summary>
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    [Indexed]
    public string RaceId { get; set; } = string.Empty;

    public string ParticipantId { get; set; } = string.Empty;

    public int LapsCompleted { get; set; }

    public bool IsOnFinalLap { get; set; }

    public RaceParticipantStatus Status { get; set; }

    public DateTimeOffset? FinishTime { get; set; }

    public double? ElapsedSeconds { get; set; }

    /// <summary>When the committee made the entry (UTC epoch ms) — the server keeps the newest per boat.</summary>
    public long UpdatedAtMs { get; set; }

    public static string KeyOf(string raceId, string participantId) => $"{raceId}:{participantId}";
}

using SQLite;

namespace SailRacing.Models;

public enum RaceParticipantStatus
{
    Racing,
    Finished,
    Dnf,
    Dns,
    Ret,
    Ocs
}

/// <summary>
/// A participant's entry in a specific race: their lap target/progress and result.
/// </summary>
public class RaceParticipant
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string RaceId { get; set; } = string.Empty;

    [Indexed]
    public string ParticipantId { get; set; } = string.Empty;

    public int Laps { get; set; }

    public int LapsCompleted { get; set; }

    public bool IsOnFinalLap { get; set; }

    public RaceParticipantStatus Status { get; set; } = RaceParticipantStatus.Racing;

    public DateTimeOffset? FinishTime { get; set; }

    public double? ElapsedSeconds { get; set; }

    public double? CorrectedSeconds { get; set; }

    public int? Rank { get; set; }
}

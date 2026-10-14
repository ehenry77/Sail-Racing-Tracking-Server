using SQLite;

namespace SailRacing.Models;

/// <summary>One row per race (1:1) — keyed by RaceId rather than its own Id.</summary>
public class StartLine
{
    [PrimaryKey]
    public string RaceId { get; set; } = string.Empty;

    public double? CommitteeLatitude { get; set; }

    public double? CommitteeLongitude { get; set; }

    public double? PinLatitude { get; set; }

    public double? PinLongitude { get; set; }
}

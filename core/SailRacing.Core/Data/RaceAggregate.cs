using SailRacing.Models;

namespace SailRacing.Data;

/// <summary>A Race plus everything that makes up its course and entry list — the unit the app reads/writes/syncs as a whole.</summary>
public class RaceAggregate
{
    public Race Race { get; set; } = new();

    public StartLine StartLine { get; set; } = new();

    public List<Buoy> Buoys { get; set; } = new();

    public List<RaceParticipant> RaceParticipants { get; set; } = new();
}

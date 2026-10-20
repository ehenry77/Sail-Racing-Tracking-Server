using SailRacing.Models;

namespace SailRacing.ViewModels;

/// <summary>A row of the race list: the race, and how it is shown.</summary>
public sealed class RaceListItem
{
    public RaceListItem(Race race)
    {
        Race = race;
    }

    public Race Race { get; }

    public string Name => Race.Name;

    public RaceStatus Status => Race.Status;

    public DateTimeOffset? ScheduledStartTime => Race.ScheduledStartTime;

    /// <summary>"Race code K7P2M", or a note that the server has not assigned one yet.</summary>
    public string CodeText => string.IsNullOrEmpty(Race.JoinCode)
        ? "Race code: not assigned yet (assigned once the race has synced)"
        : $"Race code {Race.JoinCode}";
}

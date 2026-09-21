namespace SailRacing.Services;

public enum SequenceEventType
{
    GetReadyAlarm,
    CountdownTick,
    ClassFlagUp,
    PFlagUp,
    PFlagDown,
    ClassFlagDown
}

/// <summary>One entry in the precomputed start-sequence timeline, relative to T0 (the start time).</summary>
public sealed record SequenceEvent(TimeSpan Offset, SequenceEventType Type, int? CountdownNumber = null);

public static class StartSequenceTimeline
{
    private static readonly (TimeSpan Offset, SequenceEventType Type)[] FlagEvents =
    {
        (TimeSpan.FromMinutes(-5), SequenceEventType.ClassFlagUp),
        (TimeSpan.FromMinutes(-4), SequenceEventType.PFlagUp),
        (TimeSpan.FromMinutes(-1), SequenceEventType.PFlagDown),
        (TimeSpan.Zero, SequenceEventType.ClassFlagDown)
    };

    /// <summary>
    /// Builds the full sorted timeline: standalone get-ready alarms at T-10:00/T-6:00, then for each of the
    /// four RRS flag events, a 10-second spoken countdown immediately preceding it.
    /// </summary>
    public static IReadOnlyList<SequenceEvent> Build()
    {
        var events = new List<SequenceEvent>
        {
            new(TimeSpan.FromMinutes(-10), SequenceEventType.GetReadyAlarm),
            new(TimeSpan.FromMinutes(-6), SequenceEventType.GetReadyAlarm)
        };

        foreach (var (offset, type) in FlagEvents)
        {
            for (var secondsBefore = 10; secondsBefore >= 1; secondsBefore--)
            {
                events.Add(new SequenceEvent(offset - TimeSpan.FromSeconds(secondsBefore), SequenceEventType.CountdownTick, secondsBefore));
            }

            events.Add(new SequenceEvent(offset, type));
        }

        return events.OrderBy(e => e.Offset).ToList();
    }

    public static string SpokenText(SequenceEvent evt) => evt.Type switch
    {
        SequenceEventType.GetReadyAlarm => "Attention. Get ready.",
        SequenceEventType.CountdownTick => evt.CountdownNumber.ToString()!,
        SequenceEventType.ClassFlagUp => "Class flag up. Five minutes.",
        SequenceEventType.PFlagUp => "Preparatory flag up. Four minutes.",
        SequenceEventType.PFlagDown => "Preparatory flag down. One minute.",
        SequenceEventType.ClassFlagDown => "Class flag down. Start.",
        _ => string.Empty
    };
}

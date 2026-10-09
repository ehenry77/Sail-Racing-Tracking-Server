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

/// <summary>One entry in the precomputed start-sequence timeline, relative to T0 (the start signal),
/// with the words to speak when it fires.</summary>
public sealed record SequenceEvent(TimeSpan Offset, SequenceEventType Type, int? CountdownNumber = null, string Text = "");

public static class StartSequenceTimeline
{
    /// <summary>
    /// Builds the full sorted timeline from the committee's settings: the get-ready alarms, then for each
    /// of the four signals a spoken countdown immediately before it followed by the signal itself.
    /// </summary>
    public static IReadOnlyList<SequenceEvent> Build(StartSequenceSettings settings)
    {
        var events = new List<SequenceEvent>();

        foreach (var minutes in settings.AlarmMinutes)
        {
            events.Add(new SequenceEvent(
                OffsetOf(minutes),
                SequenceEventType.GetReadyAlarm,
                Text: $"Attention. {SpokenTimeToStart(minutes)} to start. Get ready."));
        }

        var signals = new (double Minutes, SequenceEventType Type, string Text)[]
        {
            (settings.ClassFlagUpMinutes, SequenceEventType.ClassFlagUp, $"Class flag up. {SpokenTimeToStart(settings.ClassFlagUpMinutes)}."),
            (settings.PFlagUpMinutes, SequenceEventType.PFlagUp, $"Preparatory flag up. {SpokenTimeToStart(settings.PFlagUpMinutes)}."),
            (settings.PFlagDownMinutes, SequenceEventType.PFlagDown, $"Preparatory flag down. {SpokenTimeToStart(settings.PFlagDownMinutes)}."),
            (0, SequenceEventType.ClassFlagDown, "Class flag down. Start.")
        };

        foreach (var (minutes, type, text) in signals)
        {
            var offset = OffsetOf(minutes);
            for (var secondsBefore = settings.CountdownSeconds; secondsBefore >= 1; secondsBefore--)
            {
                events.Add(new SequenceEvent(
                    offset - TimeSpan.FromSeconds(secondsBefore),
                    SequenceEventType.CountdownTick,
                    secondsBefore,
                    secondsBefore.ToString()));
            }

            events.Add(new SequenceEvent(offset, type, Text: text));
        }

        return events.OrderBy(e => e.Offset).ToList();
    }

    // Whole seconds, so fractional minutes ("-0.5") don't pick up floating-point noise.
    private static TimeSpan OffsetOf(double minutes) => TimeSpan.FromSeconds(Math.Round(minutes * 60));

    /// <summary>"5 minutes", "1 minute", "30 seconds", "1 minute 30 seconds" — how long until the start.</summary>
    public static string SpokenTimeToStart(double minutesBeforeStart)
    {
        var totalSeconds = (int)Math.Round(Math.Abs(minutesBeforeStart) * 60);
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;

        var parts = new List<string>();
        if (minutes > 0) parts.Add(minutes == 1 ? "1 minute" : $"{minutes} minutes");
        if (seconds > 0) parts.Add(seconds == 1 ? "1 second" : $"{seconds} seconds");
        return string.Join(" ", parts);
    }
}

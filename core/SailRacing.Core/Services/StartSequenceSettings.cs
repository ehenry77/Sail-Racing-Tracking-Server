using System.Globalization;

namespace SailRacing.Services;

/// <summary>
/// The tunable parts of the start sequence. Times are minutes before the start signal (negative), and the
/// start itself is always 0 — it's the reference every other time is measured from.
/// </summary>
public sealed record StartSequenceSettings(
    IReadOnlyList<double> AlarmMinutes,
    int CountdownSeconds,
    double ClassFlagUpMinutes,
    double PFlagUpMinutes,
    double PFlagDownMinutes)
{
    public const string DefaultAlarmsText = "-10,-6";
    public const string DefaultCountdownText = "10";
    public const string DefaultSignalsText = "-5,-4,-1,0";

    public const int MaxCountdownSeconds = 30;

    public static StartSequenceSettings Default { get; } = new(new[] { -10.0, -6.0 }, 10, -5, -4, -1);

    /// <summary>How long before the start the whole sequence begins — the earliest alarm or signal.
    /// "Begin Start Sequence" with no scheduled time sets the start this far ahead of now.</summary>
    public TimeSpan LeadTime
    {
        get
        {
            var earliest = Math.Min(ClassFlagUpMinutes, AlarmMinutes.Count > 0 ? AlarmMinutes.Min() : 0);
            return TimeSpan.FromSeconds(Math.Round(-earliest * 60));
        }
    }

    /// <summary>One-line summary, shown in Settings so a typo is obvious before the race.</summary>
    public string Describe()
    {
        var alarms = AlarmMinutes.Count == 0 ? "none" : string.Join(", ", AlarmMinutes.Select(FormatOffset));
        var countdown = CountdownSeconds == 0 ? "no spoken countdown" : $"{CountdownSeconds} s spoken countdown before each signal";
        return $"Alarms: {alarms} · Class flag up {FormatOffset(ClassFlagUpMinutes)} · P flag up {FormatOffset(PFlagUpMinutes)} · " +
               $"P flag down {FormatOffset(PFlagDownMinutes)} · Start T-0:00 · {countdown}";
    }

    public static string FormatOffset(double minutes)
    {
        var seconds = (int)Math.Round(Math.Abs(minutes) * 60);
        var sign = seconds == 0 ? "-" : minutes < 0 ? "-" : "+";
        return $"T{sign}{seconds / 60}:{seconds % 60:00}";
    }

    /// <summary>
    /// Parses what the committee typed. Times may be written with or without the minus sign ("-5,-4,-1,0"
    /// or "5,4,1,0") and use '.' for fractions of a minute. Returns false with a plain-language reason when
    /// something would make the sequence nonsensical or let a countdown talk over another announcement.
    /// </summary>
    public static bool TryParse(string alarmsText, string countdownText, string signalsText,
        out StartSequenceSettings settings, out string error)
    {
        settings = Default;

        if (!TryParseList(alarmsText, out var alarms, out error, "alarm times"))
        {
            return false;
        }

        if (!TryParseList(signalsText, out var signals, out error, "signal times"))
        {
            return false;
        }

        if (!int.TryParse(countdownText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var countdown)
            || countdown < 0 || countdown > MaxCountdownSeconds)
        {
            error = $"Countdown must be a whole number of seconds from 0 to {MaxCountdownSeconds} (0 turns it off).";
            return false;
        }

        if (signals.Count != 4)
        {
            error = "Signals need four times: class flag up, P flag up, P flag down, start — for example -5,-4,-1,0.";
            return false;
        }

        if (signals[3] != 0)
        {
            error = "The last signal is the start, so it must be 0.";
            return false;
        }

        if (!(signals[0] < signals[1] && signals[1] < signals[2] && signals[2] < signals[3]))
        {
            error = "Signal times must run in order, earliest first (class flag up, P flag up, P flag down, start).";
            return false;
        }

        if (alarms.Any(a => a >= 0))
        {
            error = "Alarms must be before the start (a time below 0).";
            return false;
        }

        // A signal's countdown runs for the seconds before it; it mustn't reach back into the previous
        // announcement, or the numbers would be spoken over it.
        var all = alarms.Concat(signals).Distinct().OrderBy(t => t).ToList();
        if (alarms.Intersect(signals).Any())
        {
            error = "An alarm can't be at the same time as a signal.";
            return false;
        }

        if (countdown > 0)
        {
            foreach (var signal in signals)
            {
                var index = all.IndexOf(signal);
                if (index == 0)
                {
                    continue;
                }

                var gapSeconds = (signal - all[index - 1]) * 60;
                if (gapSeconds < countdown + 2)
                {
                    error = $"The {countdown} s countdown before {FormatOffset(signal)} would overlap the previous " +
                            $"announcement at {FormatOffset(all[index - 1])}. Shorten the countdown or space them further apart.";
                    return false;
                }
            }
        }

        settings = new StartSequenceSettings(alarms.Distinct().OrderBy(a => a).ToList(), countdown, signals[0], signals[1], signals[2]);
        error = string.Empty;
        return true;
    }

    private static bool TryParseList(string text, out List<double> values, out string error, string what)
    {
        values = new List<double>();
        error = string.Empty;

        foreach (var part in (text ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            {
                error = $"\"{part}\" isn't a number in the {what}. Use minutes, separated by commas — for example -5,-4,-1,0.";
                return false;
            }

            // "5" and "-5" both mean five minutes before the start.
            values.Add(value == 0 ? 0 : -Math.Abs(value));
        }

        return true;
    }
}

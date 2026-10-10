using CommunityToolkit.Mvvm.ComponentModel;
using SailRacing.Models;

namespace SailRacing.ViewModels;

public partial class ResultsEntry : ObservableObject
{
    public string ParticipantId { get; init; } = string.Empty;

    public string ParticipantName { get; init; } = string.Empty;

    public string Helm { get; init; } = string.Empty;

    public double Tcf { get; init; } = 1.0;

    public RaceParticipantStatus Status { get; init; }

    public DateTimeOffset? FinishTime { get; init; }

    /// <summary>Real (clock) time from the start signal to the finish.</summary>
    public double? ElapsedSeconds { get; init; }

    /// <summary>Handicap time: elapsed x TCF. Only finishers have one.</summary>
    public double? CorrectedSeconds { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RankText))]
    private int? rank;

    public string RankText => Rank?.ToString() ?? "–";

    public string TcfText => Tcf.ToString("0.000");

    public string FinishClockText => FinishTime is { } t ? t.ToLocalTime().ToString("HH:mm:ss") : "–";

    public string ElapsedText => FormatDuration(ElapsedSeconds);

    public string CorrectedText => FormatDuration(CorrectedSeconds);

    /// <summary>h:mm:ss, rounded to the nearest second; "–" when there is no time (didn't finish).</summary>
    public static string FormatDuration(double? seconds)
    {
        if (seconds is not { } s)
        {
            return "–";
        }

        var t = TimeSpan.FromSeconds(Math.Round(s));
        return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
    }
}

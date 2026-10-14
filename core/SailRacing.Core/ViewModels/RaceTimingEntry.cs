using CommunityToolkit.Mvvm.ComponentModel;
using SailRacing.Models;

namespace SailRacing.ViewModels;

public partial class RaceTimingEntry : ObservableObject
{
    public string ParticipantId { get; init; } = string.Empty;

    public string ParticipantName { get; init; } = string.Empty;

    public double Tcf { get; init; }

    public int Laps { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRecordLap))]
    private int lapsCompleted;

    [ObservableProperty]
    private bool isOnFinalLap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFinished))]
    [NotifyPropertyChangedFor(nameof(CanRecordLap))]
    private RaceParticipantStatus status = RaceParticipantStatus.Racing;

    public bool IsFinished => Status == RaceParticipantStatus.Finished;

    /// <summary>A lap can only be recorded while the boat is still racing and has laps left — once the
    /// last lap is done the boat is finished and the lap button is disabled.</summary>
    public bool CanRecordLap => Status == RaceParticipantStatus.Racing && LapsCompleted < Laps;

    [ObservableProperty]
    private DateTimeOffset? finishTime;

    [ObservableProperty]
    private double? elapsedSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJoinUrl))]
    [NotifyPropertyChangedFor(nameof(JoinLinkText))]
    private string? joinUrl;

    public bool HasJoinUrl => !string.IsNullOrEmpty(JoinUrl);

    public string JoinLinkText => JoinUrl ?? Services.JoinLinks.NotSyncedText;
}

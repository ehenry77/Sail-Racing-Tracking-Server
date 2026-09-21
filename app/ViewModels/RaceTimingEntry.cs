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
    private int lapsCompleted;

    [ObservableProperty]
    private bool isOnFinalLap;

    [ObservableProperty]
    private RaceParticipantStatus status = RaceParticipantStatus.Racing;

    [ObservableProperty]
    private DateTimeOffset? finishTime;

    [ObservableProperty]
    private double? elapsedSeconds;
}

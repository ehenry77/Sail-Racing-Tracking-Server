using CommunityToolkit.Mvvm.ComponentModel;
using SailRacing.Models;

namespace SailRacing.ViewModels;

public partial class ResultsEntry : ObservableObject
{
    public string ParticipantId { get; init; } = string.Empty;

    public string ParticipantName { get; init; } = string.Empty;

    public RaceParticipantStatus Status { get; init; }

    public double? ElapsedSeconds { get; init; }

    public double? CorrectedSeconds { get; init; }

    [ObservableProperty]
    private int? rank;
}

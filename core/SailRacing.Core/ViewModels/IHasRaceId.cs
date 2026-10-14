namespace SailRacing.ViewModels;

/// <summary>A screen that works on one race, told which by navigation ("raceSetup?raceId=…").</summary>
public interface IHasRaceId
{
    string? RaceId { get; set; }
}

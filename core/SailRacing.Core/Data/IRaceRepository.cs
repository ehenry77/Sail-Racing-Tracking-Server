using SailRacing.Models;

namespace SailRacing.Data;

public interface IRaceRepository
{
    Task<List<Race>> GetAllAsync();

    Task<RaceAggregate?> GetAggregateAsync(string raceId);

    Task SaveAggregateAsync(RaceAggregate aggregate);

    Task DeleteAsync(Race race);

    /// <summary>
    /// Sets only the race's start-video location. A single-column update on purpose: it is called from a background
    /// service while screens save the race, and saving a whole (possibly older) copy would undo their changes.
    /// </summary>
    Task SetStartVideoPathAsync(string raceId, string? path);
}

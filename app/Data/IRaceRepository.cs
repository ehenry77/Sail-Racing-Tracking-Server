using SailRacing.Models;

namespace SailRacing.Data;

public interface IRaceRepository
{
    Task<List<Race>> GetAllAsync();

    Task<RaceAggregate?> GetAggregateAsync(string raceId);

    Task SaveAggregateAsync(RaceAggregate aggregate);

    Task DeleteAsync(Race race);
}

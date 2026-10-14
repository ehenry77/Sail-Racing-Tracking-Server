using SailRacing.Models;

namespace SailRacing.Data;

public class RaceRepository : IRaceRepository
{
    private readonly ISailRacingDatabase _database;

    public RaceRepository(ISailRacingDatabase database)
    {
        _database = database;
    }

    public async Task<List<Race>> GetAllAsync()
    {
        await _database.InitializeAsync();
        return await _database.Connection.Table<Race>().OrderByDescending(r => r.CreatedAt).ToListAsync();
    }

    public async Task<RaceAggregate?> GetAggregateAsync(string raceId)
    {
        await _database.InitializeAsync();

        var race = await _database.Connection.Table<Race>().Where(r => r.Id == raceId).FirstOrDefaultAsync();
        if (race is null)
        {
            return null;
        }

        var startLine = await _database.Connection.Table<StartLine>()
            .Where(s => s.RaceId == raceId)
            .FirstOrDefaultAsync() ?? new StartLine { RaceId = raceId };

        var buoys = await _database.Connection.Table<Buoy>()
            .Where(b => b.RaceId == raceId)
            .OrderBy(b => b.Sequence)
            .ToListAsync();

        var raceParticipants = await _database.Connection.Table<RaceParticipant>()
            .Where(rp => rp.RaceId == raceId)
            .ToListAsync();

        return new RaceAggregate
        {
            Race = race,
            StartLine = startLine,
            Buoys = buoys,
            RaceParticipants = raceParticipants
        };
    }

    public async Task SaveAggregateAsync(RaceAggregate aggregate)
    {
        await _database.InitializeAsync();

        aggregate.Race.UpdatedAt = DateTimeOffset.UtcNow;
        var existingRace = await _database.Connection.Table<Race>()
            .Where(r => r.Id == aggregate.Race.Id)
            .FirstOrDefaultAsync();
        if (existingRace is null)
        {
            await _database.Connection.InsertAsync(aggregate.Race);
        }
        else
        {
            await _database.Connection.UpdateAsync(aggregate.Race);
        }

        aggregate.StartLine.RaceId = aggregate.Race.Id;
        var existingStartLine = await _database.Connection.Table<StartLine>()
            .Where(s => s.RaceId == aggregate.Race.Id)
            .FirstOrDefaultAsync();
        if (existingStartLine is null)
        {
            await _database.Connection.InsertAsync(aggregate.StartLine);
        }
        else
        {
            await _database.Connection.UpdateAsync(aggregate.StartLine);
        }

        var existingBuoys = await _database.Connection.Table<Buoy>()
            .Where(b => b.RaceId == aggregate.Race.Id)
            .ToListAsync();
        foreach (var buoy in existingBuoys)
        {
            await _database.Connection.DeleteAsync(buoy);
        }
        foreach (var buoy in aggregate.Buoys)
        {
            buoy.RaceId = aggregate.Race.Id;
            await _database.Connection.InsertAsync(buoy);
        }

        var existingRaceParticipants = await _database.Connection.Table<RaceParticipant>()
            .Where(rp => rp.RaceId == aggregate.Race.Id)
            .ToListAsync();
        var incomingParticipantIds = aggregate.RaceParticipants.Select(rp => rp.ParticipantId).ToHashSet();

        foreach (var stale in existingRaceParticipants.Where(rp => !incomingParticipantIds.Contains(rp.ParticipantId)))
        {
            await _database.Connection.DeleteAsync(stale);
        }

        foreach (var rp in aggregate.RaceParticipants)
        {
            rp.RaceId = aggregate.Race.Id;
            var existing = existingRaceParticipants.FirstOrDefault(e => e.ParticipantId == rp.ParticipantId);
            if (existing is null)
            {
                await _database.Connection.InsertAsync(rp);
            }
            else
            {
                rp.Id = existing.Id;
                await _database.Connection.UpdateAsync(rp);
            }
        }
    }

    public async Task DeleteAsync(Race race)
    {
        await _database.InitializeAsync();

        var startLine = await _database.Connection.Table<StartLine>().Where(s => s.RaceId == race.Id).FirstOrDefaultAsync();
        if (startLine is not null)
        {
            await _database.Connection.DeleteAsync(startLine);
        }

        var buoys = await _database.Connection.Table<Buoy>().Where(b => b.RaceId == race.Id).ToListAsync();
        foreach (var buoy in buoys)
        {
            await _database.Connection.DeleteAsync(buoy);
        }

        var raceParticipants = await _database.Connection.Table<RaceParticipant>().Where(rp => rp.RaceId == race.Id).ToListAsync();
        foreach (var rp in raceParticipants)
        {
            await _database.Connection.DeleteAsync(rp);
        }

        await _database.Connection.DeleteAsync(race);
    }
}

using SailRacing.Models;

namespace SailRacing.Data;

public class FleetRepository : IFleetRepository
{
    private readonly ISailRacingDatabase _database;

    public FleetRepository(ISailRacingDatabase database)
    {
        _database = database;
    }

    public async Task<List<Fleet>> GetAllAsync()
    {
        await _database.InitializeAsync();
        return await _database.Connection.Table<Fleet>().OrderBy(f => f.Name).ToListAsync();
    }

    public async Task<Fleet?> GetByIdAsync(string id)
    {
        await _database.InitializeAsync();
        return await _database.Connection.Table<Fleet>().Where(f => f.Id == id).FirstOrDefaultAsync();
    }

    public async Task<List<string>> GetParticipantIdsAsync(string fleetId)
    {
        await _database.InitializeAsync();
        var links = await _database.Connection.Table<FleetParticipant>()
            .Where(fp => fp.FleetId == fleetId)
            .ToListAsync();
        return links.Select(l => l.ParticipantId).ToList();
    }

    public async Task SaveAsync(Fleet fleet, IEnumerable<string> participantIds)
    {
        await _database.InitializeAsync();

        var existing = await GetByIdAsync(fleet.Id);
        if (existing is null)
        {
            await _database.Connection.InsertAsync(fleet);
        }
        else
        {
            await _database.Connection.UpdateAsync(fleet);
        }

        var existingLinks = await _database.Connection.Table<FleetParticipant>()
            .Where(fp => fp.FleetId == fleet.Id)
            .ToListAsync();
        foreach (var link in existingLinks)
        {
            await _database.Connection.DeleteAsync(link);
        }

        foreach (var participantId in participantIds)
        {
            await _database.Connection.InsertAsync(new FleetParticipant
            {
                FleetId = fleet.Id,
                ParticipantId = participantId
            });
        }
    }

    public async Task DeleteAsync(Fleet fleet)
    {
        await _database.InitializeAsync();
        var links = await _database.Connection.Table<FleetParticipant>()
            .Where(fp => fp.FleetId == fleet.Id)
            .ToListAsync();
        foreach (var link in links)
        {
            await _database.Connection.DeleteAsync(link);
        }
        await _database.Connection.DeleteAsync(fleet);
    }
}

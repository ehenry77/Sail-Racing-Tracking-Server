using SailRacing.Models;

namespace SailRacing.Data;

public class ParticipantRepository : IParticipantRepository
{
    private readonly ISailRacingDatabase _database;

    public ParticipantRepository(ISailRacingDatabase database)
    {
        _database = database;
    }

    public async Task<List<Participant>> GetAllAsync()
    {
        await _database.InitializeAsync();
        return await _database.Connection.Table<Participant>().OrderBy(p => p.Name).ToListAsync();
    }

    public async Task<Participant?> GetByIdAsync(string id)
    {
        await _database.InitializeAsync();
        return await _database.Connection.Table<Participant>().Where(p => p.Id == id).FirstOrDefaultAsync();
    }

    public async Task<List<Participant>> GetByIdsAsync(IEnumerable<string> ids)
    {
        await _database.InitializeAsync();
        var idSet = ids.ToHashSet();
        var all = await _database.Connection.Table<Participant>().ToListAsync();
        return all.Where(p => idSet.Contains(p.Id)).ToList();
    }

    public async Task SaveAsync(Participant participant)
    {
        await _database.InitializeAsync();
        participant.UpdatedAt = DateTimeOffset.UtcNow;
        var existing = await GetByIdAsync(participant.Id);
        if (existing is null)
        {
            await _database.Connection.InsertAsync(participant);
        }
        else
        {
            await _database.Connection.UpdateAsync(participant);
        }
    }

    public async Task DeleteAsync(Participant participant)
    {
        await _database.InitializeAsync();
        await _database.Connection.DeleteAsync(participant);
    }
}

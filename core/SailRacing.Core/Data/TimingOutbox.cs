using SailRacing.Models;

namespace SailRacing.Data;

/// <summary>The local queue of manual timing entries waiting to be sent to the server.</summary>
public class TimingOutbox
{
    private readonly ISailRacingDatabase _database;

    public TimingOutbox(ISailRacingDatabase database)
    {
        _database = database;
    }

    /// <summary>Stores the entry, replacing any unsent older one for the same boat.</summary>
    public async Task EnqueueAsync(PendingTimingUpdate update)
    {
        await _database.InitializeAsync();
        update.Key = PendingTimingUpdate.KeyOf(update.RaceId, update.ParticipantId);
        await _database.Connection.InsertOrReplaceAsync(update);
    }

    public async Task<List<PendingTimingUpdate>> GetAllAsync()
    {
        await _database.InitializeAsync();
        return await _database.Connection.Table<PendingTimingUpdate>().ToListAsync();
    }

    public async Task<HashSet<string>> GetParticipantIdsAsync(string raceId)
    {
        await _database.InitializeAsync();
        var rows = await _database.Connection.Table<PendingTimingUpdate>().Where(p => p.RaceId == raceId).ToListAsync();
        return rows.Select(r => r.ParticipantId).ToHashSet();
    }

    /// <summary>Removes an entry once the server has it — unless a newer one replaced it while it was in flight.</summary>
    /// <summary>Drops every queued entry of a race — used when the race is deleted from this PC.</summary>
    public async Task RemoveRaceAsync(string raceId)
    {
        await _database.InitializeAsync();
        await _database.Connection.ExecuteAsync("DELETE FROM PendingTimingUpdate WHERE RaceId = ?", raceId);
    }

    public async Task RemoveIfUnchangedAsync(PendingTimingUpdate sent)
    {
        await _database.InitializeAsync();
        var current = await _database.Connection.FindAsync<PendingTimingUpdate>(sent.Key);
        if (current is not null && current.UpdatedAtMs <= sent.UpdatedAtMs)
        {
            await _database.Connection.DeleteAsync(current);
        }
    }
}

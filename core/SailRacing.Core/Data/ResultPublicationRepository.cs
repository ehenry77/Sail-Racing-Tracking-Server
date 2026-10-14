using SailRacing.Models;

namespace SailRacing.Data;

public class ResultPublicationRepository : IResultPublicationRepository
{
    private readonly ISailRacingDatabase _database;

    public ResultPublicationRepository(ISailRacingDatabase database)
    {
        _database = database;
    }

    public async Task<List<ResultPublication>> GetPendingAsync()
    {
        await _database.InitializeAsync();
        return await _database.Connection.Table<ResultPublication>()
            .Where(r => r.ServerAckStatus != PublishAckStatus.Success)
            .ToListAsync();
    }

    public async Task SaveAsync(ResultPublication publication)
    {
        await _database.InitializeAsync();
        var existing = await _database.Connection.Table<ResultPublication>()
            .Where(r => r.Id == publication.Id)
            .FirstOrDefaultAsync();
        if (existing is null)
        {
            await _database.Connection.InsertAsync(publication);
        }
        else
        {
            await _database.Connection.UpdateAsync(publication);
        }
    }
}

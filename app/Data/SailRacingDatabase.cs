using SailRacing.Models;
using SQLite;

namespace SailRacing.Data;

public class SailRacingDatabase : ISailRacingDatabase
{
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public SQLiteAsyncConnection Connection { get; }

    public SailRacingDatabase()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "sailracing.db3");
        Connection = new SQLiteAsyncConnection(dbPath);
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_initialized)
            {
                return;
            }

            await Connection.CreateTableAsync<Participant>();
            await Connection.CreateTableAsync<Fleet>();
            await Connection.CreateTableAsync<FleetParticipant>();
            await Connection.CreateTableAsync<Race>();
            await Connection.CreateTableAsync<RaceParticipant>();
            await Connection.CreateTableAsync<Buoy>();
            await Connection.CreateTableAsync<StartLine>();
            await Connection.CreateTableAsync<ResultPublication>();

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }
}

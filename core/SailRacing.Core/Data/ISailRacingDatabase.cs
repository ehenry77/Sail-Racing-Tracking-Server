using SQLite;

namespace SailRacing.Data;

/// <summary>Owns the single SQLite connection and schema creation for the committee app.</summary>
public interface ISailRacingDatabase
{
    SQLiteAsyncConnection Connection { get; }

    Task InitializeAsync();
}

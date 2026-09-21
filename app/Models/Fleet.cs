using SQLite;

namespace SailRacing.Models;

public class Fleet
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class FleetParticipant
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string FleetId { get; set; } = string.Empty;

    [Indexed]
    public string ParticipantId { get; set; } = string.Empty;
}

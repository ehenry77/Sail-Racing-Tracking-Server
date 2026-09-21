using SQLite;

namespace SailRacing.Models;

public class Buoy
{
    [PrimaryKey]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Indexed]
    public string RaceId { get; set; } = string.Empty;

    public int Sequence { get; set; }

    public string Name { get; set; } = string.Empty;

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public bool CapturedViaGps { get; set; }
}

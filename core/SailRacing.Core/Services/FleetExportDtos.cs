namespace SailRacing.Services;

// Local file interchange format for Fleets/Competitors — independent of the server wire contract
// in /shared/contracts, since this never leaves the device except via user-initiated share/import.

public class ParticipantExportDto
{
    public string Name { get; set; } = string.Empty;
    public string Helm { get; set; } = string.Empty;
    public double Tcf { get; set; } = 1.0;
}

public class FleetExportDto
{
    public string Name { get; set; } = string.Empty;
    public List<ParticipantExportDto> Participants { get; set; } = new();
}

namespace SailRacing.Services;

// Wire DTOs matching /shared/contracts/*.schema.json. Kept separate from the local SQLite
// Models so the on-device schema and the server contract can evolve independently.

public class ParticipantDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Helm { get; set; } = string.Empty;
    public double Tcf { get; set; }
}

public class FleetDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> ParticipantIds { get; set; } = new();
    public List<ParticipantDto> Participants { get; set; } = new();
}

public class BuoyDto
{
    public string Id { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public string Name { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool CapturedViaGps { get; set; }
}

public class StartLineDto
{
    public double? CommitteeLatitude { get; set; }
    public double? CommitteeLongitude { get; set; }
    public double? PinLatitude { get; set; }
    public double? PinLongitude { get; set; }
}

public class RaceParticipantDto
{
    public string ParticipantId { get; set; } = string.Empty;
    public int Laps { get; set; }
    public int LapsCompleted { get; set; }
    public bool IsOnFinalLap { get; set; }
    public string Status { get; set; } = "Racing";
    public DateTimeOffset? FinishTime { get; set; }
    public double? ElapsedSeconds { get; set; }
    public double? CorrectedSeconds { get; set; }
    public int? Rank { get; set; }
}

public class RaceDto
{
    public string Id { get; set; } = string.Empty;
    public int ContractVersion { get; set; } = 1;
    public string? JoinCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "Setup";
    public FleetDto Fleet { get; set; } = new();
    public StartLineDto StartLine { get; set; } = new();
    public bool FinishSameAsStart { get; set; } = true;
    public double? FinishLatitude { get; set; }
    public double? FinishLongitude { get; set; }
    public List<BuoyDto> Buoys { get; set; } = new();
    public int LapsDefault { get; set; }
    public List<RaceParticipantDto> RaceParticipants { get; set; } = new();
    public DateTimeOffset? StartAt { get; set; }
    public DateTimeOffset? ShortenCourseAppliedAt { get; set; }
}

public class CreateRaceResponse
{
    public string RaceId { get; set; } = string.Empty;
    public string JoinCode { get; set; } = string.Empty;
}

public class ResultEntryDto
{
    public string ParticipantId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public double? ElapsedSeconds { get; set; }
    public double? CorrectedSeconds { get; set; }
    public int? Rank { get; set; }
}

public class ResultPublicationDto
{
    public string RaceId { get; set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; set; }
    public List<ResultEntryDto> Results { get; set; } = new();
}

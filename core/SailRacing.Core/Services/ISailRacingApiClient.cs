namespace SailRacing.Services;

public interface ISailRacingApiClient
{
    Task<CreateRaceResponse> CreateRaceAsync(RaceDto race, CancellationToken ct = default);

    Task UpdateRaceAsync(RaceDto race, CancellationToken ct = default);

    Task ShortenCourseAsync(string raceId, List<RaceParticipantDto> newLaps, CancellationToken ct = default);

    Task StartSequenceAsync(string raceId, DateTimeOffset startAt, CancellationToken ct = default);

    Task AllClearAsync(string raceId, CancellationToken ct = default);

    Task PublishResultsAsync(ResultPublicationDto payload, CancellationToken ct = default);

    /// <summary>Sends manual timing entries; the server keeps the newest per boat. Throws when unreachable.</summary>
    Task<TimingPushResponse> PostTimingAsync(string raceId, List<BoatTimingDto> boats, CancellationToken ct = default);

    /// <summary>The race as the server holds it, found by its join code; null if there is no such race.</summary>
    Task<RaceDto?> GetRaceByCodeAsync(string joinCode, CancellationToken ct = default);

    /// <summary>The race as the server holds it; null if the server doesn't know it.</summary>
    Task<RaceDto?> GetRaceAsync(string raceId, CancellationToken ct = default);
}

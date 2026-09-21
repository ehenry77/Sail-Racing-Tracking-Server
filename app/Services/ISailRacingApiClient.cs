namespace SailRacing.Services;

public interface ISailRacingApiClient
{
    Task<CreateRaceResponse> CreateRaceAsync(RaceDto race, CancellationToken ct = default);

    Task UpdateRaceAsync(RaceDto race, CancellationToken ct = default);

    Task ShortenCourseAsync(string raceId, List<RaceParticipantDto> newLaps, CancellationToken ct = default);

    Task StartSequenceAsync(string raceId, DateTimeOffset startAt, CancellationToken ct = default);

    Task AllClearAsync(string raceId, CancellationToken ct = default);

    Task PublishResultsAsync(ResultPublicationDto payload, CancellationToken ct = default);
}

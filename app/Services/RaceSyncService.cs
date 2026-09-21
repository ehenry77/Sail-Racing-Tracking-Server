using SailRacing.Data;
using SailRacing.Models;

namespace SailRacing.Services;

/// <summary>
/// Outbox pattern: pushes a race to the server, marking Race.SyncStatus so a failure (flaky venue
/// wifi) can be retried later without blocking the committee UI on network success.
/// </summary>
public class RaceSyncService : IRaceSyncService
{
    private readonly IRaceRepository _races;
    private readonly IFleetRepository _fleets;
    private readonly IParticipantRepository _participants;
    private readonly ISailRacingApiClient _api;
    private bool _watcherStarted;

    public RaceSyncService(
        IRaceRepository races,
        IFleetRepository fleets,
        IParticipantRepository participants,
        ISailRacingApiClient api)
    {
        _races = races;
        _fleets = fleets;
        _participants = participants;
        _api = api;
    }

    public async Task<bool> PushAsync(string raceId, CancellationToken ct = default)
    {
        var aggregate = await _races.GetAggregateAsync(raceId);
        if (aggregate is null)
        {
            return false;
        }

        try
        {
            var dto = await BuildDtoAsync(aggregate);

            if (string.IsNullOrEmpty(aggregate.Race.RemoteRaceId))
            {
                var created = await _api.CreateRaceAsync(dto, ct);
                aggregate.Race.RemoteRaceId = created.RaceId;
                aggregate.Race.JoinCode = created.JoinCode;
            }
            else
            {
                dto.JoinCode = aggregate.Race.JoinCode;
                await _api.UpdateRaceAsync(dto, ct);
            }

            aggregate.Race.SyncStatus = SyncStatus.Synced;
            await _races.SaveAggregateAsync(aggregate);
            return true;
        }
        catch
        {
            aggregate.Race.SyncStatus = SyncStatus.Failed;
            await _races.SaveAggregateAsync(aggregate);
            return false;
        }
    }

    public async Task RetryPendingAsync(CancellationToken ct = default)
    {
        var races = await _races.GetAllAsync();
        foreach (var race in races.Where(r => r.SyncStatus != SyncStatus.Synced))
        {
            await PushAsync(race.Id, ct);
        }
    }

    public void StartConnectivityWatcher()
    {
        if (_watcherStarted)
        {
            return;
        }

        _watcherStarted = true;
        Connectivity.Current.ConnectivityChanged += async (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
            {
                await RetryPendingAsync();
            }
        };
    }

    private async Task<RaceDto> BuildDtoAsync(RaceAggregate aggregate)
    {
        var fleet = await _fleets.GetByIdAsync(aggregate.Race.FleetId);
        var participantIds = fleet is null
            ? new List<string>()
            : await _fleets.GetParticipantIdsAsync(fleet.Id);
        var participants = await _participants.GetByIdsAsync(participantIds);

        return new RaceDto
        {
            Id = aggregate.Race.Id,
            JoinCode = aggregate.Race.JoinCode,
            Name = aggregate.Race.Name,
            Status = aggregate.Race.Status.ToString(),
            Fleet = new FleetDto
            {
                Id = fleet?.Id ?? string.Empty,
                Name = fleet?.Name ?? string.Empty,
                ParticipantIds = participantIds,
                Participants = participants
                    .Select(p => new ParticipantDto { Id = p.Id, Name = p.Name, Helm = p.Helm, Tcf = p.Tcf })
                    .ToList()
            },
            StartLine = new StartLineDto
            {
                CommitteeLatitude = aggregate.StartLine.CommitteeLatitude,
                CommitteeLongitude = aggregate.StartLine.CommitteeLongitude,
                PinLatitude = aggregate.StartLine.PinLatitude,
                PinLongitude = aggregate.StartLine.PinLongitude
            },
            FinishSameAsStart = aggregate.Race.FinishSameAsStart,
            FinishLatitude = aggregate.Race.FinishLatitude,
            FinishLongitude = aggregate.Race.FinishLongitude,
            Buoys = aggregate.Buoys
                .Select(b => new BuoyDto
                {
                    Id = b.Id,
                    Sequence = b.Sequence,
                    Name = b.Name,
                    Latitude = b.Latitude,
                    Longitude = b.Longitude,
                    CapturedViaGps = b.CapturedViaGps
                })
                .ToList(),
            LapsDefault = aggregate.Race.LapsDefault,
            RaceParticipants = aggregate.RaceParticipants
                .Select(rp => new RaceParticipantDto
                {
                    ParticipantId = rp.ParticipantId,
                    Laps = rp.Laps,
                    LapsCompleted = rp.LapsCompleted,
                    IsOnFinalLap = rp.IsOnFinalLap,
                    Status = rp.Status.ToString(),
                    FinishTime = rp.FinishTime,
                    ElapsedSeconds = rp.ElapsedSeconds,
                    CorrectedSeconds = rp.CorrectedSeconds,
                    Rank = rp.Rank
                })
                .ToList(),
            StartAt = aggregate.Race.StartAt,
            ShortenCourseAppliedAt = aggregate.Race.ShortenCourseAppliedAt
        };
    }
}

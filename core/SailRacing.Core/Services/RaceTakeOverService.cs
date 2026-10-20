using SailRacing.Data;
using SailRacing.Models;

namespace SailRacing.Services;

public class TakeOverResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>The taken-over race, ready to open.</summary>
    public Race? Race { get; init; }
}

public interface IRaceTakeOverService
{
    /// <summary>
    /// Rebuilds a race on this PC from the server, found by its join code (or a pasted join/map/replay link),
    /// so a committee PC can carry on a race that another one started. Never throws.
    /// </summary>
    Task<TakeOverResult> TakeOverAsync(string joinCodeOrLink, CancellationToken ct = default);
}

/// <summary>
/// "Take over a race": the server holds the whole race — course, boats, start time, lap counts, finishes —
/// so any committee PC can pull it down and continue. The race keeps its id and join code, which is what
/// keeps every competitor's link and the live map working. Fleet and boats are created here if this PC has
/// never seen them.
/// </summary>
public class RaceTakeOverService : IRaceTakeOverService
{
    private readonly IRaceRepository _races;
    private readonly IFleetRepository _fleets;
    private readonly IParticipantRepository _participants;
    private readonly ISailRacingApiClient _api;
    private readonly IRaceSyncService _sync;

    public RaceTakeOverService(
        IRaceRepository races,
        IFleetRepository fleets,
        IParticipantRepository participants,
        ISailRacingApiClient api,
        IRaceSyncService sync)
    {
        _races = races;
        _fleets = fleets;
        _participants = participants;
        _api = api;
        _sync = sync;
    }

    public async Task<TakeOverResult> TakeOverAsync(string joinCodeOrLink, CancellationToken ct = default)
    {
        var code = ExtractJoinCode(joinCodeOrLink);
        if (string.IsNullOrEmpty(code))
        {
            return Fail("Enter the race's join code (5 letters and digits), or paste its link.");
        }

        RaceDto? dto;
        try
        {
            // Entries typed on this PC that never reached the server must not be overwritten by the copy
            // we are about to pull: send them first. If the server can't be reached the pull fails too.
            await _sync.FlushTimingAsync(ct);
            dto = await _api.GetRaceByCodeAsync(code, ct);
        }
        catch
        {
            return Fail("Could not reach the server. Check the connection and the server address in Settings.");
        }

        if (dto is null)
        {
            return Fail($"The server has no race with the code {code}.");
        }

        try
        {
            var race = await StoreLocallyAsync(dto);
            return new TakeOverResult
            {
                Success = true,
                Race = race,
                Message = $"Took over \"{race.Name}\" ({race.Status})."
            };
        }
        catch (Exception ex)
        {
            return Fail($"Could not store the race on this PC: {ex.Message}");
        }
    }

    private static TakeOverResult Fail(string message) => new() { Success = false, Message = message };

    /// <summary>
    /// Accepts "ABCDE", "abcde", "abc de" or any of the race's links
    /// (".../race/ABCDE?boat=…", ".../map/?code=ABCDE", ".../replay/?code=ABCDE").
    /// </summary>
    public static string ExtractJoinCode(string input)
    {
        var text = (input ?? string.Empty).Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var fromQuery = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .FirstOrDefault(kv => kv.Length == 2 && kv[0].Equals("code", StringComparison.OrdinalIgnoreCase));
            if (fromQuery is not null)
            {
                text = Uri.UnescapeDataString(fromQuery[1]);
            }
            else
            {
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var raceIndex = Array.FindIndex(segments, s => s.Equals("race", StringComparison.OrdinalIgnoreCase));
                text = raceIndex >= 0 && raceIndex + 1 < segments.Length ? segments[raceIndex + 1] : string.Empty;
            }
        }

        return new string(text.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private async Task<Race> StoreLocallyAsync(RaceDto dto)
    {
        // Fleet and boats first, with the server's ids, so the race's references resolve.
        var participantIds = new List<string>();
        foreach (var p in dto.Fleet.Participants)
        {
            var participant = await _participants.GetByIdAsync(p.Id) ?? new Participant { Id = p.Id };
            participant.Name = p.Name;
            participant.Helm = p.Helm;
            participant.Tcf = p.Tcf;
            await _participants.SaveAsync(participant);
            participantIds.Add(p.Id);
        }

        var fleetId = string.IsNullOrEmpty(dto.Fleet.Id) ? Guid.NewGuid().ToString() : dto.Fleet.Id;
        var fleet = await _fleets.GetByIdAsync(fleetId) ?? new Fleet { Id = fleetId };
        fleet.Name = dto.Fleet.Name;
        await _fleets.SaveAsync(fleet, participantIds);

        // The race itself: keep this PC's local-only settings (scheduled start time) if it already had the
        // race, and take everything the server holds.
        var aggregate = await _races.GetAggregateAsync(dto.Id) ?? new RaceAggregate { Race = new Race { Id = dto.Id } };
        var race = aggregate.Race;
        race.Name = dto.Name;
        race.FleetId = fleetId;
        race.Status = ParseOrDefault(dto.Status, RaceStatus.Setup);
        race.LapsDefault = dto.LapsDefault;
        race.StartAt = dto.StartAt;
        race.ShortenCourseAppliedAt = dto.ShortenCourseAppliedAt;
        race.FinishSameAsStart = dto.FinishSameAsStart;
        race.FinishLatitude = dto.FinishLatitude;
        race.FinishLongitude = dto.FinishLongitude;
        race.RemoteRaceId = dto.Id;
        race.JoinCode = dto.JoinCode;
        race.SyncStatus = SyncStatus.Synced;

        aggregate.StartLine.CommitteeLatitude = dto.StartLine.CommitteeLatitude;
        aggregate.StartLine.CommitteeLongitude = dto.StartLine.CommitteeLongitude;
        aggregate.StartLine.PinLatitude = dto.StartLine.PinLatitude;
        aggregate.StartLine.PinLongitude = dto.StartLine.PinLongitude;

        aggregate.Buoys = dto.Buoys
            .OrderBy(b => b.Sequence)
            .Select(b => new Buoy
            {
                Id = b.Id,
                Sequence = b.Sequence,
                Name = b.Name,
                Latitude = b.Latitude,
                Longitude = b.Longitude,
                CapturedViaGps = b.CapturedViaGps
            })
            .ToList();

        // The photos live only on this PC; a refresh from the server must not forget them.
        var photos = aggregate.RaceParticipants
            .Where(p => !string.IsNullOrEmpty(p.FinishPhotoPath))
            .ToDictionary(p => p.ParticipantId, p => p.FinishPhotoPath);

        aggregate.RaceParticipants = dto.RaceParticipants
            .Select(rp => new RaceParticipant
            {
                ParticipantId = rp.ParticipantId,
                Laps = rp.Laps,
                LapsCompleted = rp.LapsCompleted,
                IsOnFinalLap = rp.IsOnFinalLap,
                Status = ParseOrDefault(rp.Status, RaceParticipantStatus.Racing),
                FinishTime = rp.FinishTime,
                ElapsedSeconds = rp.ElapsedSeconds,
                CorrectedSeconds = rp.CorrectedSeconds,
                Rank = rp.Rank,
                FinishPhotoPath = photos.GetValueOrDefault(rp.ParticipantId)
            })
            .ToList();

        await _races.SaveAggregateAsync(aggregate);
        return race;
    }

    private static T ParseOrDefault<T>(string? value, T fallback) where T : struct =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}

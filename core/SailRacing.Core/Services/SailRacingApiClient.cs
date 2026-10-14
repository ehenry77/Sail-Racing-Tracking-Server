using System.Net.Http.Json;
using System.Text.Json;

namespace SailRacing.Services;

/// <summary>
/// Thin REST client for the Node.js server. Builds absolute URLs from <see cref="AppConfig.ServerBaseUrl"/>
/// per call rather than an HttpClient.BaseAddress, so the target server can change at runtime without
/// re-registering the DI client.
/// </summary>
public class SailRacingApiClient : ISailRacingApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public SailRacingApiClient(HttpClient http)
    {
        _http = http;
    }

    private Uri Url(string path) => new(new Uri(AppConfig.ServerBaseUrl), path);

    public async Task<CreateRaceResponse> CreateRaceAsync(RaceDto race, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(Url("/api/races"), race, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateRaceResponse>(JsonOptions, ct))!;
    }

    public async Task UpdateRaceAsync(RaceDto race, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync(Url($"/api/races/{race.Id}"), race, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ShortenCourseAsync(string raceId, List<RaceParticipantDto> newLaps, CancellationToken ct = default)
    {
        var response = await _http.PatchAsync(
            Url($"/api/races/{raceId}/shorten-course"),
            JsonContent.Create(new { newLaps }, options: JsonOptions),
            ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task StartSequenceAsync(string raceId, DateTimeOffset startAt, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(Url($"/api/races/{raceId}/start-sequence"), new { startAt }, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task AllClearAsync(string raceId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync(Url($"/api/races/{raceId}/all-clear"), content: null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task PublishResultsAsync(ResultPublicationDto payload, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(Url($"/api/races/{payload.RaceId}/results"), payload, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<TimingPushResponse> PostTimingAsync(string raceId, List<BoatTimingDto> boats, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(Url($"/api/races/{Uri.EscapeDataString(raceId)}/timing"), new { boats }, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TimingPushResponse>(JsonOptions, ct)) ?? new TimingPushResponse();
    }

    public Task<RaceDto?> GetRaceByCodeAsync(string joinCode, CancellationToken ct = default) =>
        GetRaceAtAsync($"/api/races/by-code/{Uri.EscapeDataString(joinCode)}", ct);

    public Task<RaceDto?> GetRaceAsync(string raceId, CancellationToken ct = default) =>
        GetRaceAtAsync($"/api/races/{Uri.EscapeDataString(raceId)}", ct);

    private async Task<RaceDto?> GetRaceAtAsync(string path, CancellationToken ct)
    {
        var response = await _http.GetAsync(Url(path), ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RaceDto>(JsonOptions, ct);
    }
}

using System.Text.Json;

namespace SailRacing.Services;

public class RaceSocketMessage
{
    public string Type { get; init; } = string.Empty;

    public JsonElement Raw { get; init; }
}

/// <summary>WebSocket client for a race, per /shared/contracts/websocket-events.md.</summary>
public interface IRaceSocketClient
{
    event EventHandler<RaceSocketMessage>? MessageReceived;

    Task ConnectAsync(string raceId, string role, string? participantId, CancellationToken ct = default);

    Task DisconnectAsync();
}

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

    /// <summary>
    /// Connects and joins the race. Never throws — a network/connection failure (unreachable server,
    /// DNS failure, bad URL, etc.) is caught and reported via the returned bool, so a caller on the UI
    /// thread (e.g. an async-void Page.OnAppearing) can't crash the app from an unhandled exception.
    /// </summary>
    Task<bool> ConnectAsync(string raceId, string role, string? participantId, CancellationToken ct = default);

    Task DisconnectAsync();
}

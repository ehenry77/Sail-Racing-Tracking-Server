using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace SailRacing.Services;

public class RaceSocketClient : IRaceSocketClient
{
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _receiveCts;

    public event EventHandler<RaceSocketMessage>? MessageReceived;

    public async Task ConnectAsync(string raceId, string role, string? participantId, CancellationToken ct = default)
    {
        await DisconnectAsync();

        var httpBase = new Uri(AppConfig.ServerBaseUrl);
        var scheme = httpBase.Scheme == "https" ? "wss" : "ws";
        var wsUri = new UriBuilder(httpBase) { Scheme = scheme, Path = "/ws" }.Uri;

        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(wsUri, ct);

        var join = JsonSerializer.Serialize(new { type = "join", raceId, role, participantId });
        await SendRawAsync(join, ct);

        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = ReceiveLoopAsync(_receiveCts.Token);
    }

    public async Task DisconnectAsync()
    {
        _receiveCts?.Cancel();
        _receiveCts = null;

        if (_socket is { State: WebSocketState.Open })
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
            }
            catch
            {
                // best-effort close
            }
        }

        _socket?.Dispose();
        _socket = null;
    }

    private async Task SendRawAsync(string json, CancellationToken ct)
    {
        if (_socket is null)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];

        while (_socket is { State: WebSocketState.Open } && !ct.IsCancellationRequested)
        {
            try
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                stream.Position = 0;
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var type = doc.RootElement.TryGetProperty("type", out var typeProp)
                    ? typeProp.GetString() ?? string.Empty
                    : string.Empty;

                MessageReceived?.Invoke(this, new RaceSocketMessage { Type = type, Raw = doc.RootElement.Clone() });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Connection dropped; the next ConnectAsync call (e.g. re-entering the timing sheet
                // page) re-establishes it rather than looping retries in the background.
                break;
            }
        }
    }
}

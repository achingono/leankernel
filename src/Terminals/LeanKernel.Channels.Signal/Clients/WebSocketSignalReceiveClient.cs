using System.Net.WebSockets;
using System.Text;

namespace LeanKernel.Channels.Signal;

/// <summary>
/// Receives Signal payloads over the signal-cli WebSocket endpoint.
/// </summary>
public sealed class WebSocketSignalReceiveClient : ISignalReceiveClient
{
    /// <inheritdoc />
    public async Task<string?> ReceiveAsync(string account, Uri receiveUri, CancellationToken ct)
    {
        using var webSocket = new ClientWebSocket();
        await webSocket.ConnectAsync(receiveUri, ct);
        return await ReadSingleMessageAsync(webSocket, ct);
    }

    private static async Task<string?> ReadSingleMessageAsync(ClientWebSocket webSocket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var stream = new MemoryStream();

        while (!ct.IsCancellationRequested)
        {
            var result = await webSocket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (result.CloseStatus.HasValue && result.CloseStatus.Value != WebSocketCloseStatus.NormalClosure)
                {
                    var detail = string.IsNullOrWhiteSpace(result.CloseStatusDescription)
                        ? $"Signal WebSocket closed with status {result.CloseStatus.Value}."
                        : $"Signal WebSocket closed with status {result.CloseStatus.Value}: {result.CloseStatusDescription}";
                    throw new WebSocketException(WebSocketError.Faulted, detail);
                }

                break;
            }

            if (result.Count > 0)
            {
                await stream.WriteAsync(buffer.AsMemory(0, result.Count), ct);
            }

            if (result.EndOfMessage)
            {
                break;
            }
        }

        if (stream.Length == 0)
        {
            return null;
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
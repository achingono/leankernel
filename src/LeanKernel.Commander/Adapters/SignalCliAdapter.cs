using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Commander.Adapters;

/// <summary>
/// Manages the signal-cli process in JSON-RPC mode.
/// Sends JSON-RPC requests and reads line-delimited JSON responses.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class SignalCliAdapter : IAsyncDisposable
{
    private readonly string _cliPath;
    private readonly string _account;
    private readonly ILogger _logger;
    private Process? _process;
    private CancellationTokenSource? _cts;

    public event Action<SignalInboundMessage>? OnMessage;
    public event Action<string>? OnError;

    public SignalCliAdapter(string cliPath, string account, ILogger logger)
    {
        _cliPath = cliPath;
        _account = account;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _cliPath,
                Arguments = $"-a {_account} jsonRpc",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };

        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _logger.LogWarning("signal-cli stderr: {Line}", e.Data);
                OnError?.Invoke(e.Data);
            }
        };

        try
        {
            _process.Start();
            _process.BeginErrorReadLine();
            _logger.LogInformation("signal-cli started (PID: {Pid})", _process.Id);

            _ = Task.Run(() => ReadOutputLoopAsync(_cts.Token), _cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start signal-cli at {Path}", _cliPath);
            throw;
        }

        await Task.CompletedTask;
    }

    public async Task SendMessageAsync(string recipient, string message, CancellationToken ct)
    {
        var request = new JsonRpcRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Method = "send",
            Params = new SendParams
            {
                Recipient = [recipient],
                Message = message
            }
        };

        var json = JsonSerializer.Serialize(request, SignalJsonContext.Default.JsonRpcRequest);
        await WriteAsync(json, ct);
    }

    private async Task WriteAsync(string json, CancellationToken ct)
    {
        if (_process?.StandardInput is null)
        {
            _logger.LogWarning("signal-cli process not available for writing");
            return;
        }

        await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);
        _logger.LogDebug("Sent to signal-cli: {Json}", json);
    }

    private async Task ReadOutputLoopAsync(CancellationToken ct)
    {
        if (_process?.StandardOutput is null) return;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _process.StandardOutput.ReadLineAsync(ct);
                if (line is null) break; // process exited

                ProcessLine(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading signal-cli output");
        }

        _logger.LogInformation("signal-cli output reader stopped");
    }

    private void ProcessLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        try
        {
            var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            // JSON-RPC notification (no "id" or "id" is null) = incoming message
            if (root.TryGetProperty("method", out var method) &&
                method.GetString() == "receive")
            {
                if (root.TryGetProperty("params", out var parameters))
                {
                    ParseInboundMessage(parameters);
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse signal-cli output: {Line}", line);
        }
    }

    private void ParseInboundMessage(JsonElement parameters)
    {
        var envelope = parameters.GetProperty("envelope");
        var source = envelope.TryGetProperty("source", out var s) ? s.GetString() : null;
        var dataMessage = envelope.TryGetProperty("dataMessage", out var dm) ? dm : (JsonElement?)null;

        if (source is null || dataMessage is null) return;

        var body = dataMessage.Value.TryGetProperty("message", out var msg) ? msg.GetString() : null;
        var timestamp = dataMessage.Value.TryGetProperty("timestamp", out var ts) ? ts.GetInt64() : 0;

        if (string.IsNullOrEmpty(body)) return;

        OnMessage?.Invoke(new SignalInboundMessage
        {
            Sender = source,
            Body = body,
            TimestampMs = timestamp
        });
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();

        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            catch { }
        }

        _process?.Dispose();
        _cts?.Dispose();
    }
}

[ExcludeFromCodeCoverage]
public sealed record SignalInboundMessage
{
    public required string Sender { get; init; }
    public required string Body { get; init; }
    public long TimestampMs { get; init; }
}

// JSON-RPC models for signal-cli communication
[ExcludeFromCodeCoverage]
internal sealed record JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; init; } = "2.0";
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("method")] public required string Method { get; init; }
    [JsonPropertyName("params")] public required object Params { get; init; }
}

[ExcludeFromCodeCoverage]
internal sealed record SendParams
{
    [JsonPropertyName("recipient")] public required string[] Recipient { get; init; }
    [JsonPropertyName("message")] public required string Message { get; init; }
}

[ExcludeFromCodeCoverage]
[JsonSerializable(typeof(JsonRpcRequest))]
[JsonSerializable(typeof(SendParams))]
internal partial class SignalJsonContext : JsonSerializerContext;

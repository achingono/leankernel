using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Models;

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
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingRequests = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
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
        await SendRequestAsync("send", new Dictionary<string, object?>
        {
            ["recipient"] = new[] { recipient },
            ["message"] = message
        }, ct);
    }

    public async Task SendTypingAsync(string recipient, bool stop, CancellationToken ct)
    {
        var @params = new Dictionary<string, object?>
        {
            ["recipient"] = recipient
        };

        if (stop)
            @params["stop"] = true;

        // sendTyping is fire-and-forget — send as a notification (no id, no response expected)
        await SendNotificationAsync("sendTyping", @params, ct);
    }

    private async Task<JsonElement> SendRequestAsync(string method, object @params, CancellationToken ct)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var request = new JsonRpcRequest
        {
            Id = requestId,
            Method = method,
            Params = @params
        };

        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(requestId, tcs))
            throw new InvalidOperationException($"Duplicate signal-cli request id generated: {requestId}");

        using var registration = ct.Register(() =>
        {
            if (_pendingRequests.TryRemove(requestId, out var pending))
                pending.TrySetCanceled(ct);
        });

        try
        {
            var json = JsonSerializer.Serialize(request);
            await WriteAsync(json, ct);
            return await tcs.Task.WaitAsync(ct);
        }
        catch
        {
            _pendingRequests.TryRemove(requestId, out _);
            throw;
        }
    }

    private async Task SendNotificationAsync(string method, object @params, CancellationToken ct)
    {
        var notification = new JsonRpcNotification { Method = method, Params = @params };
        var json = JsonSerializer.Serialize(notification);
        await WriteAsync(json, ct);
    }

    private static readonly TimeSpan AttachmentFetchTimeout = TimeSpan.FromSeconds(30);

    private async Task<byte[]> GetAttachmentBytesAsync(
        string attachmentId,
        string sender,
        string? groupId,
        CancellationToken ct)
    {
        Dictionary<string, object?> requestParams = new()
        {
            ["id"] = attachmentId
        };

        if (!string.IsNullOrWhiteSpace(groupId))
            requestParams["groupId"] = groupId;
        else
            requestParams["recipient"] = sender;

        using var timeoutCts = new CancellationTokenSource(AttachmentFetchTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var result = await SendRequestAsync("getAttachment", requestParams, linkedCts.Token);
        if (result.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Unexpected getAttachment result kind: {result.ValueKind}");

        return Convert.FromBase64String(result.GetString() ?? string.Empty);
    }

    private async Task WriteAsync(string json, CancellationToken ct)
    {
        if (_process?.StandardInput is null)
        {
            _logger.LogWarning("signal-cli process not available for writing");
            return;
        }

        await _writeLock.WaitAsync(ct);
        try
        {
            await _process.StandardInput.WriteLineAsync(json.AsMemory(), ct);
            await _process.StandardInput.FlushAsync(ct);
            _logger.LogDebug("Sent to signal-cli: {Json}", json);
        }
        finally
        {
            _writeLock.Release();
        }
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

                ProcessLine(line, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading signal-cli output");
        }

        _logger.LogInformation("signal-cli output reader stopped");
    }

    private void ProcessLine(string line, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        try
        {
            var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (TryCompletePendingRequest(root))
                return;

            if (root.TryGetProperty("method", out var method) &&
                method.GetString() == "receive")
            {
                if (root.TryGetProperty("params", out var parameters))
                {
                    var parametersClone = parameters.Clone();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var inbound = await ParseInboundMessageAsync(parametersClone, ct);
                            if (inbound is not null)
                                OnMessage?.Invoke(inbound);
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to process Signal inbound attachment payload");
                        }
                    }, ct);
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse signal-cli output: {Line}", line);
        }
    }

    private bool TryCompletePendingRequest(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var idProperty))
            return false;

        var requestId = idProperty.ValueKind switch
        {
            JsonValueKind.String => idProperty.GetString(),
            JsonValueKind.Number => idProperty.GetRawText(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(requestId))
            return false;

        if (!_pendingRequests.TryRemove(requestId, out var pending))
            return true;

        if (root.TryGetProperty("error", out var error))
        {
            pending.TrySetException(new InvalidOperationException(
                $"signal-cli returned an error: {error.GetRawText()}"));
            return true;
        }

        if (root.TryGetProperty("result", out var result))
        {
            pending.TrySetResult(result.Clone());
            return true;
        }

        pending.TrySetException(new InvalidOperationException("signal-cli response did not contain a result or error."));
        return true;
    }

    private async Task<SignalInboundMessage?> ParseInboundMessageAsync(JsonElement parameters, CancellationToken ct)
    {
        JsonElement? envelope = null;

        if (parameters.TryGetProperty("envelope", out var directEnvelope))
            envelope = directEnvelope;
        else if (parameters.TryGetProperty("result", out var result)
                 && result.TryGetProperty("envelope", out var wrappedEnvelope))
            envelope = wrappedEnvelope;

        if (envelope is null)
            return null;

        var source = envelope.Value.TryGetProperty("source", out var sourceProperty)
            ? sourceProperty.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(source))
            return null;

        if (!envelope.Value.TryGetProperty("dataMessage", out var dataMessage))
            return null;

        var body = dataMessage.TryGetProperty("message", out var messageProperty)
            ? messageProperty.GetString()
            : null;
        var timestamp = dataMessage.TryGetProperty("timestamp", out var timestampProperty)
            ? timestampProperty.GetInt64()
            : envelope.Value.TryGetProperty("timestamp", out var envelopeTimestamp)
                ? envelopeTimestamp.GetInt64()
                : 0;

        var groupId = dataMessage.TryGetProperty("groupInfo", out var groupInfo)
            && groupInfo.TryGetProperty("groupId", out var groupIdProperty)
                ? groupIdProperty.GetString()
                : null;

        var attachments = await ResolveAttachmentsAsync(source, groupId, dataMessage, ct);
        if (string.IsNullOrWhiteSpace(body) && attachments.Count == 0)
            return null;

        return new SignalInboundMessage
        {
            Sender = source,
            Body = body ?? string.Empty,
            TimestampMs = timestamp,
            Attachments = attachments
        };
    }

    private async Task<IReadOnlyList<InboundAttachment>> ResolveAttachmentsAsync(
        string sender,
        string? groupId,
        JsonElement dataMessage,
        CancellationToken ct)
    {
        if (!dataMessage.TryGetProperty("attachments", out var attachmentsElement)
            || attachmentsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var attachments = new List<InboundAttachment>();
        foreach (var attachment in attachmentsElement.EnumerateArray())
        {
            var attachmentId = attachment.TryGetProperty("id", out var idProperty)
                ? idProperty.GetString()
                : null;
            var fileName = attachment.TryGetProperty("filename", out var fileNameProperty)
                ? fileNameProperty.GetString()
                : null;
            var contentType = attachment.TryGetProperty("contentType", out var contentTypeProperty)
                ? contentTypeProperty.GetString()
                : null;
            var caption = attachment.TryGetProperty("caption", out var captionProperty)
                ? captionProperty.GetString()
                : null;
            var size = attachment.TryGetProperty("size", out var sizeProperty)
                ? sizeProperty.GetInt64()
                : (long?)null;

            string? extractedText = null;
            if (!string.IsNullOrWhiteSpace(attachmentId))
            {
                try
                {
                    var bytes = await GetAttachmentBytesAsync(attachmentId, sender, groupId, ct);
                    extractedText = InboundAttachmentTextExtractor.TryExtractText(contentType, fileName, bytes);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to fetch Signal attachment {AttachmentId} from {Sender}",
                        attachmentId,
                        sender);
                }
            }

            attachments.Add(new InboundAttachment
            {
                Id = attachmentId ?? string.Empty,
                FileName = fileName,
                ContentType = contentType,
                Caption = caption,
                Size = size,
                ExtractedText = extractedText
            });
        }

        return attachments;
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();

        foreach (var pending in _pendingRequests.Values)
            pending.TrySetCanceled();
        _pendingRequests.Clear();

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
        _writeLock.Dispose();
    }
}

[ExcludeFromCodeCoverage]
public sealed record SignalInboundMessage
{
    public required string Sender { get; init; }
    public required string Body { get; init; }
    public long TimestampMs { get; init; }
    public IReadOnlyList<InboundAttachment> Attachments { get; init; } = [];
}

[ExcludeFromCodeCoverage]
internal sealed record JsonRpcRequest
{
    public string JsonRpc { get; init; } = "2.0";
    public required string Id { get; init; }
    public required string Method { get; init; }
    public required object Params { get; init; }
}

[ExcludeFromCodeCoverage]
internal sealed record JsonRpcNotification
{
    public string JsonRpc { get; init; } = "2.0";
    public required string Method { get; init; }
    public required object Params { get; init; }
}

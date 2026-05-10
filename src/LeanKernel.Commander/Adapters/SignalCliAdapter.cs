using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Commander.Adapters;

/// <summary>
/// Manages the signal-cli process in JSON-RPC mode.
/// Sends JSON-RPC requests and reads line-delimited JSON responses.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class SignalCliAdapter : ISignalAdapter
{
    private static readonly TimeSpan SendMessageTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FallbackSendTimeout = TimeSpan.FromSeconds(30);

    private readonly string _cliPath;
    private readonly string _account;
    private readonly ILogger _logger;
    private readonly IAttachmentTextExtractionService _attachmentTextExtractor;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingRequests = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _restartLock = new(1, 1);
    private Process? _process;
    private CancellationTokenSource? _cts;

    public event Action<SignalInboundMessage>? OnMessage;
    public event Action<string>? OnError;

    public SignalCliAdapter(
        string cliPath,
        string account,
        ILogger logger,
        IAttachmentTextExtractionService attachmentTextExtractor)
    {
        _cliPath = cliPath;
        _account = account;
        _logger = logger;
        _attachmentTextExtractor = attachmentTextExtractor;
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
        var requestParams = new Dictionary<string, object?>
        {
            ["recipient"] = new[] { recipient },
            ["message"] = message
        };

        using var timeoutCts = new CancellationTokenSource(SendMessageTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await SendRequestAsync("send", requestParams, linkedCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "signal-cli JSON-RPC send timed out for {Recipient}; killing wedged process and falling back to direct CLI send",
                recipient);

            // Kill the wedged process so it releases the account lock before the fallback CLI send.
            await KillProcessAsync();
            await SendMessageViaCliAsync(recipient, message, ct);

            // Restart the long-lived process in the background so message receiving continues.
            _ = RestartAsync(ct);
        }
        catch (InvalidOperationException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex,
                "signal-cli JSON-RPC send failed for {Recipient}; falling back to direct CLI send",
                recipient);
            await SendMessageViaCliAsync(recipient, message, ct);
        }
    }

    private async Task KillProcessAsync()
    {
        // Fail all in-flight RPC requests immediately.
        foreach (var pending in _pendingRequests.Values)
            pending.TrySetCanceled();
        _pendingRequests.Clear();

        _cts?.Cancel();

        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error killing signal-cli process");
            }
        }

        _process?.Dispose();
        _process = null;
        _cts?.Dispose();
        _cts = null;

        _logger.LogInformation("signal-cli process killed; waiting for OS to release account lock");

        // Give the OS a moment to release the file lock before the caller spawns a new process.
        await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None);
    }

    private async Task RestartAsync(CancellationToken ct)
    {
        if (!await _restartLock.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None))
        {
            _logger.LogWarning("Could not acquire restart lock within 60 s; skipping signal-cli restart");
            return;
        }

        try
        {
            _logger.LogInformation("Restarting signal-cli JSON-RPC process...");
            await StartAsync(ct);
            _logger.LogInformation("signal-cli process restarted successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart signal-cli process");
        }
        finally
        {
            _restartLock.Release();
        }
    }

    public async Task SendTypingAsync(string recipient, bool stop, CancellationToken ct)
    {
        var @params = new Dictionary<string, object?>
        {
            ["recipient"] = recipient
        };

        if (stop)
            @params["stop"] = true;

        // signal-cli expects typing indicators as notifications without a response id.
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

    private async Task<byte[]> GetAttachmentBytesAsync(
        string attachmentId,
        CancellationToken ct)
    {
        var localAttachmentPath = ResolveLocalAttachmentPath(attachmentId);
        if (localAttachmentPath is not null)
            return await File.ReadAllBytesAsync(localAttachmentPath, ct);

        throw new FileNotFoundException(
            $"Signal attachment '{attachmentId}' was not found in the local attachment store.",
            attachmentId);
    }

    private static string? ResolveLocalAttachmentPath(string attachmentId)
    {
        if (string.IsNullOrWhiteSpace(attachmentId))
            return null;

        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "signal-cli",
                "attachments",
                attachmentId),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share",
                "signal-cli",
                "attachments",
                attachmentId)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private async Task SendMessageViaCliAsync(string recipient, string message, CancellationToken ct)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _cliPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-a");
        process.StartInfo.ArgumentList.Add(_account);
        process.StartInfo.ArgumentList.Add("send");
        process.StartInfo.ArgumentList.Add("-m");
        process.StartInfo.ArgumentList.Add(message);
        process.StartInfo.ArgumentList.Add(recipient);

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Failed to start signal-cli fallback send for {recipient}");

            using var timeoutCts = new CancellationTokenSource(FallbackSendTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            await process.WaitForExitAsync(linkedCts.Token);

            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            var stderr = await process.StandardError.ReadToEndAsync(ct);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"signal-cli fallback send failed with exit code {process.ExitCode}: {stderr}");
            }

            _logger.LogInformation("Signal message sent to {Recipient} via CLI fallback", recipient);

            if (!string.IsNullOrWhiteSpace(stdout))
                _logger.LogDebug("signal-cli fallback stdout: {Stdout}", stdout);
            if (!string.IsNullOrWhiteSpace(stderr))
                _logger.LogDebug("signal-cli fallback stderr: {Stderr}", stderr);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"signal-cli fallback send timed out after {FallbackSendTimeout.TotalSeconds:0} seconds");
        }
        finally
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogDebug(ex, "signal-cli fallback process already exited before it could be killed");
                }
            }

            process.Dispose();
        }
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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("signal-cli output reader cancelled");
        }
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

            if (TryGetReceiveParameters(root, out var parameters))
            {
                QueueInboundProcessing(parameters, ct);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse signal-cli output: {Line}", line);
        }
    }

    private static bool TryGetReceiveParameters(JsonElement root, out JsonElement parameters)
    {
        parameters = default;
        return root.TryGetProperty("method", out var method)
            && method.GetString() == "receive"
            && root.TryGetProperty("params", out parameters);
    }

    private void QueueInboundProcessing(JsonElement parameters, CancellationToken ct)
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogDebug("Signal inbound processing cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process Signal inbound attachment payload");
            }
        }, ct);
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
        var timestamp = GetSignalTimestamp(dataMessage, envelope.Value);

        var attachments = await ResolveAttachmentsAsync(source, dataMessage, ct);
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
            var fileName = GetOptionalString(attachment, "filename");
            var contentType = GetOptionalString(attachment, "contentType");
            var caption = GetOptionalString(attachment, "caption");
            var size = attachment.TryGetProperty("size", out var sizeProperty)
                ? sizeProperty.GetInt64()
                : (long?)null;
            var extractedText = await ExtractAttachmentTextAsync(
                sender,
                attachmentId,
                fileName,
                contentType,
                ct);

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

    private async Task<string?> ExtractAttachmentTextAsync(
        string sender,
        string? attachmentId,
        string? fileName,
        string? contentType,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(attachmentId)
            || !_attachmentTextExtractor.CanExtractText(contentType, fileName))
        {
            return null;
        }

        try
        {
            var bytes = await GetAttachmentBytesAsync(attachmentId, ct);
            return await _attachmentTextExtractor.ExtractTextAsync(
                contentType,
                fileName,
                bytes,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch Signal attachment {AttachmentId} from {Sender}",
                attachmentId,
                sender);
            return null;
        }
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
            catch (InvalidOperationException ex)
            {
                _logger.LogDebug(ex, "signal-cli process already exited before disposal could kill it");
            }
        }

        _process?.Dispose();
        _cts?.Dispose();
        _writeLock.Dispose();
        _restartLock.Dispose();
    }

    private static long GetSignalTimestamp(JsonElement dataMessage, JsonElement envelope)
    {
        if (dataMessage.TryGetProperty("timestamp", out var timestampProperty))
            return timestampProperty.GetInt64();

        return envelope.TryGetProperty("timestamp", out var envelopeTimestamp)
            ? envelopeTimestamp.GetInt64()
            : 0;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) ? property.GetString() : null;
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

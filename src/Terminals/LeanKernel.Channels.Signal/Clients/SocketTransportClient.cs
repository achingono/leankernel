using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;

using LeanKernel.Channels.Signal.HealthChecks;

using Microsoft.Extensions.Options;

namespace LeanKernel.Channels.Signal;

/// <summary>
/// Signal transport client that communicates with the signal-cli REST API and WebSocket endpoint.
/// </summary>
public sealed class SocketTransportClient(
    IHttpClientFactory httpClientFactory,
    IOptions<SignalSettings> settings,
    IChannelCredentialProvider credentials,
    ISignalReceiveClient signalReceiveClient,
    ILogger<SocketTransportClient> logger) : ITransportClient, ISocketWorkerHealthProvider, IHostedService, IAsyncDisposable
{
    private sealed record AccountWorker(CancellationTokenSource Cancellation, Task Task);

    private readonly object _sync = new();
    private readonly Queue<InboundMessage> _pending = new();
    private readonly SemaphoreSlim _pendingSignal = new(0);
    private readonly Dictionary<string, AccountWorker> _workers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AccountWorkerHealthTracker> _workerHealth = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _lifecycleCts;
    private Task? _managerTask;
    private bool _started;
    private bool _disposed;
    private DateTime _startedUtc;
    private int _consecutiveEmptyDiscoveryResults;
    private volatile bool _initialDiscoveryCompleted;

    /// <summary>
    /// Starts account worker management.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_started)
            {
                return Task.CompletedTask;
            }

            _lifecycleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _managerTask = Task.Run(() => ManageWorkersAsync(_lifecycleCts.Token), CancellationToken.None);
            _started = true;
            _startedUtc = DateTime.UtcNow;
        }

        logger.LogInformation("Signal transport started.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops account worker management and disposes active worker loops.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? managerTask;
        CancellationTokenSource? lifecycleCts;

        lock (_sync)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            managerTask = _managerTask;
            _managerTask = null;

            lifecycleCts = _lifecycleCts;
            _lifecycleCts = null;
        }

        lifecycleCts?.Cancel();

        if (managerTask is not null)
        {
            await WaitForTaskAsync(managerTask, cancellationToken);
        }

        lifecycleCts?.Dispose();
        logger.LogInformation("Signal transport stopped.");
    }

    /// <summary>
    /// Receives the next inbound Signal message from the bounded in-memory queue.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The next inbound message, or <c>null</c> when canceled.</returns>
    public async Task<InboundMessage?> ReceiveAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (TryDequeueInbound(out var inbound))
            {
                return inbound;
            }

            await _pendingSignal.WaitAsync(ct);
        }

        return null;
    }

    /// <summary>
    /// Sends a text message with optional text styles to a Signal recipient.
    /// </summary>
    /// <param name="account">The sending account identifier.</param>
    /// <param name="recipient">The recipient identifier.</param>
    /// <param name="text">The message text.</param>
    /// <param name="textStyles">The text styles to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when send succeeded; otherwise <c>false</c>.</returns>
    public async Task<bool> SendAsync(string account, string recipient, string text, IReadOnlyList<SignalTextStyle> textStyles, CancellationToken ct)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient(Constants.HttpClientNames.SignalApi);
            var payload = new
            {
                number = account,
                recipients = new[] { recipient },
                message = text,
                textStyles = textStyles.Count > 0 ? textStyles : null
            };

            using var response = await httpClient.PostAsJsonAsync("/v2/send", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Signal send failed for account {Account} recipient {Recipient} with status {StatusCode}.",
                    account,
                    recipient,
                    response.StatusCode);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "Signal send timed out for account {Account} recipient {Recipient}.",
                account,
                recipient);
            return false;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "Signal send failed due to HTTP error for account {Account} recipient {Recipient}.",
                account,
                recipient);
            return false;
        }
    }

    /// <summary>
    /// Sends a typing indicator start notification to the recipient.
    /// </summary>
    /// <param name="account">The sending account identifier.</param>
    /// <param name="recipient">The recipient identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task StartTypingAsync(string account, string recipient, CancellationToken ct) =>
        SendTypingIndicatorAsync(account, recipient, stop: false, ct);

    /// <summary>
    /// Sends a typing indicator stop notification to the recipient.
    /// </summary>
    /// <param name="account">The sending account identifier.</param>
    /// <param name="recipient">The recipient identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task StopTypingAsync(string account, string recipient, CancellationToken ct) =>
        SendTypingIndicatorAsync(account, recipient, stop: true, ct);

    /// <summary>
    /// Disposes transport resources.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await StopAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Signal transport failed to stop during dispose.");
        }

        _pendingSignal.Dispose();
    }

    /// <summary>
    /// Returns an immutable snapshot of per-account worker health state.
    /// </summary>
    /// <returns>A dictionary keyed by account number.</returns>
    public IReadOnlyDictionary<string, SocketWorkerHealthState> GetWorkerStates()
    {
        lock (_sync)
        {
            var snapshot = new Dictionary<string, SocketWorkerHealthState>(_workerHealth.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _workerHealth)
            {
                var tracker = pair.Value;
                snapshot[pair.Key] = new SocketWorkerHealthState(
                    pair.Key,
                    tracker.State,
                    tracker.LastSuccessfulReceiveUtc,
                    tracker.LastWorkerLoopTickUtc,
                    tracker.StartedUtc,
                    tracker.ConsecutiveErrors,
                    tracker.LastErrorUtc);
            }

            return snapshot;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the initial account discovery completed successfully.
    /// </summary>
    public bool IsInitialDiscoveryCompleted => _initialDiscoveryCompleted;

    /// <summary>
    /// Gets the UTC timestamp when transport startup began.
    /// </summary>
    public DateTime StartedUtc
    {
        get
        {
            lock (_sync)
            {
                return _startedUtc;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the signal socket transport has been started.
    /// </summary>
    public bool IsTransportStarted
    {
        get
        {
            lock (_sync)
            {
                return _started;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the worker manager task is still running.
    /// </summary>
    public bool IsManagerRunning
    {
        get
        {
            lock (_sync)
            {
                return _started && _managerTask is { IsCompleted: false };
            }
        }
    }

    /// <summary>
    /// Returns an atomic manager lifecycle snapshot.
    /// </summary>
    /// <returns>A tuple containing transport started and manager running values.</returns>
    public (bool Started, bool Running) GetManagerState()
    {
        lock (_sync)
        {
            return (_started, _managerTask is { IsCompleted: false });
        }
    }

    private async Task ManageWorkersAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await ManageWorkersCycleAsync(ct);
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Signal account worker manager crashed; restarting.");
                    await DelayReconnectAsync(ct);
                }
            }
        }
        finally
        {
            await StopAllWorkersAsync();
        }
    }

    private async Task ManageWorkersCycleAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await RefreshWorkersAsync(ct);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, settings.Value.AccountRefreshSeconds)), ct);
        }
    }

    private async Task RefreshWorkersAsync(CancellationToken ct)
    {
        var (discoverySucceeded, discovered) = await DiscoverConfiguredAccountsAsync(ct);
        if (!discoverySucceeded)
        {
            _consecutiveEmptyDiscoveryResults = 0;
            logger.LogWarning("Signal account discovery failed; preserving existing workers.");
            return;
        }

        _initialDiscoveryCompleted = true;

        var desiredAccounts = new HashSet<string>(
            discovered.Where(IsAccountName),
            StringComparer.OrdinalIgnoreCase);

        var hasActiveWorkers = false;
        lock (_sync)
        {
            hasActiveWorkers = _workers.Count > 0;
        }

        if (desiredAccounts.Count == 0 && hasActiveWorkers)
        {
            _consecutiveEmptyDiscoveryResults++;
            if (_consecutiveEmptyDiscoveryResults < 2)
            {
                logger.LogWarning(
                    "Signal account discovery returned no accounts while workers were active; preserving existing workers (streak={EmptyStreak}).",
                    _consecutiveEmptyDiscoveryResults);
                return;
            }

            logger.LogWarning(
                "Signal account discovery returned no accounts for {EmptyStreak} consecutive refreshes; deprovisioning workers.",
                _consecutiveEmptyDiscoveryResults);
        }
        else
        {
            _consecutiveEmptyDiscoveryResults = 0;
        }

        if (desiredAccounts.Count == 0)
        {
            logger.LogWarning("No Signal accounts were discovered from signal-cli /v1/accounts.");
        }

        List<string> accountsToStart;
        List<(string Account, AccountWorker Worker)> workersToStop;

        lock (_sync)
        {
            workersToStop = _workers
                .Where(pair => !desiredAccounts.Contains(pair.Key))
                .Select(static pair => (pair.Key, pair.Value))
                .ToList();

            foreach (var (account, _) in workersToStop)
            {
                _workers.Remove(account);
                _workerHealth.TryRemove(account, out _);
            }

            accountsToStart = desiredAccounts
                .Where(account => !_workers.ContainsKey(account))
                .OrderBy(static account => account, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var account in accountsToStart)
            {
                _workerHealth[account] = new AccountWorkerHealthTracker();
                var workerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var workerTask = Task.Run(() => RunAccountWorkerAsync(account, workerCts.Token), CancellationToken.None);
                _workers[account] = new AccountWorker(workerCts, workerTask);
            }
        }

        foreach (var (account, worker) in workersToStop)
        {
            logger.LogInformation("Stopping Signal account worker for {Account}.", account);
            worker.Cancellation.Cancel();
            await WaitForTaskAsync(worker.Task, CancellationToken.None);
            worker.Cancellation.Dispose();
        }

        foreach (var account in accountsToStart)
        {
            logger.LogInformation("Started Signal account worker for {Account}.", account);
        }
    }

    private async Task RunAccountWorkerAsync(string account, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var tracker = GetHealthTracker(account);
            tracker?.MarkLoopTick();

            try
            {
                var payload = await ReceiveViaWebSocketAsync(account, ct);
                if (string.IsNullOrWhiteSpace(payload))
                {
                    await DelayReconnectAsync(ct);
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(payload);
                    var root = document.RootElement;
                    var received = false;

                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in root.EnumerateArray())
                        {
                            received |= await EnqueueInboundIfValidAsync(item, account, ct);
                        }
                    }
                    else
                    {
                        received |= await EnqueueInboundIfValidAsync(root, account, ct);
                    }

                    tracker?.MarkRoundTripSuccess(settings.Value.WorkerConsecutiveErrorThreshold);

                    if (received)
                    {
                        tracker?.MarkSuccessfulReceive();
                    }
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(
                        ex,
                        "Signal receive returned non-JSON payload for account {Account}: {Payload}",
                        account,
                        TruncateForLog(payload));
                    RecordWorkerError(tracker);
                    await DelayReconnectAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Signal account worker failed for {Account}; retrying.", account);
                RecordWorkerError(tracker);

                await DelayReconnectAsync(ct);
            }
        }
    }

    private void RecordWorkerError(AccountWorkerHealthTracker? tracker)
    {
        tracker?.MarkError();
        if (tracker is not null && tracker.ConsecutiveErrors >= settings.Value.WorkerUnhealthyErrorThreshold)
        {
            tracker.State = SocketWorkerState.Faulted;
        }
    }

    private async Task StopAllWorkersAsync()
    {
        List<AccountWorker> workers;

        lock (_sync)
        {
            workers = _workers.Values.ToList();
            _workers.Clear();
            _workerHealth.Clear();
        }

        foreach (var worker in workers)
        {
            worker.Cancellation.Cancel();
        }

        foreach (var worker in workers)
        {
            await WaitForTaskAsync(worker.Task, CancellationToken.None);
            worker.Cancellation.Dispose();
        }
    }

    private bool TryDequeueInbound(out InboundMessage? inbound)
    {
        lock (_sync)
        {
            if (_pending.Count == 0)
            {
                inbound = null;
                return false;
            }

            inbound = _pending.Dequeue();
            return true;
        }
    }

    private bool TryEnqueueInbound(InboundMessage inbound)
    {
        var queueCapacity = Math.Max(1, settings.Value.InboundQueueCapacity);

        lock (_sync)
        {
            if (_pending.Count >= queueCapacity)
            {
                logger.LogWarning(
                    "Dropping inbound Signal message for account {Account} sender {Sender}; reason=queue_full capacity={Capacity}.",
                    inbound.Account,
                    inbound.Sender,
                    queueCapacity);
                return false;
            }

            _pending.Enqueue(inbound);
            _pendingSignal.Release();
            return true;
        }
    }

    private async Task<string?> ReceiveViaWebSocketAsync(string account, CancellationToken ct)
    {
        var wsUri = BuildReceiveUri(account);
        using var receiveDeadlineCts = new CancellationTokenSource(GetClientReceiveDeadline());
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, receiveDeadlineCts.Token);

        try
        {
            return await signalReceiveClient.ReceiveAsync(account, wsUri, linkedCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && receiveDeadlineCts.IsCancellationRequested)
        {
            logger.LogDebug(
                "Signal receive deadline hit for account {Account} endpoint {Endpoint}; recycling receive loop.",
                account,
                wsUri);
            return null;
        }
    }

    private TimeSpan GetClientReceiveDeadline()
    {
        var configured = settings.Value.ReceiveClientDeadlineSeconds;
        if (configured <= 0)
        {
            configured = settings.Value.ReceiveTimeoutSeconds + 5;
        }

        return TimeSpan.FromSeconds(Math.Max(1, configured));
    }

    private Uri BuildReceiveUri(string account)
    {
        var scheme = settings.Value.Port == 443 ? "wss" : "ws";
        var builder = new UriBuilder(scheme, settings.Value.Host, settings.Value.Port,
            $"/v1/receive/{Uri.EscapeDataString(account)}")
        {
            Query = $"timeout={settings.Value.ReceiveTimeoutSeconds}"
        };

        return builder.Uri;
    }

    private async Task<bool> EnqueueInboundIfValidAsync(JsonElement item, string account, CancellationToken ct)
    {
        if (!TryParseSignalMessage(item, out var sender, out var text, out var attachments, logger))
        {
            logger.LogTrace(
                "Rejected Signal payload for account {Account}: {Payload}",
                account,
                BuildTracePayload(item));
            return false;
        }

        var token = await credentials.ResolveBearerTokenAsync(sender, ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("Rejecting Signal sender {Sender}; no binding token configured.", sender);
            return false;
        }

        var hydratedAttachments = await EnrichAttachmentsAsync(attachments, ct);
        return TryEnqueueInbound(new InboundMessage(account, sender, text, token, hydratedAttachments));
    }

    private async Task<(bool Success, IReadOnlyList<string> Accounts)> DiscoverConfiguredAccountsAsync(CancellationToken ct)
    {
        var httpClient = httpClientFactory.CreateClient(Constants.HttpClientNames.SignalApi);

        try
        {
            using var response = await httpClient.GetAsync("/v1/accounts", ct);
            if (!response.IsSuccessStatusCode)
            {
                return (false, Array.Empty<string>());
            }

            await using var payload = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(payload, cancellationToken: ct);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return (false, Array.Empty<string>());
            }

            var accounts = new List<string>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        accounts.Add(value);
                    }

                    continue;
                }

                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("number", out var numberElement))
                {
                    var value = numberElement.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        accounts.Add(value);
                    }
                }
            }

            return (true, accounts);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Signal account discovery from /v1/accounts failed.");
            return (false, Array.Empty<string>());
        }
    }

    private static bool IsAccountName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Regex.IsMatch(value, "^\\+?[0-9]{7,20}$");

    private async Task SendTypingIndicatorAsync(string account, string recipient, bool stop, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(recipient))
        {
            return;
        }

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, settings.Value.TypingRequestTimeoutSeconds)));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            var httpClient = httpClientFactory.CreateClient(Constants.HttpClientNames.SignalApi);
            using var request = new HttpRequestMessage(
                stop ? HttpMethod.Delete : HttpMethod.Put,
                $"/v1/typing-indicator/{Uri.EscapeDataString(account)}")
            {
                Content = JsonContent.Create(new { recipient })
            };

            using var response = await httpClient.SendAsync(request, linkedCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "Signal typing indicator returned {StatusCode} for account {Account} recipient {Recipient} (stop={Stop}).",
                    (int)response.StatusCode,
                    account,
                    recipient,
                    stop);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogDebug(
                ex,
                "Signal typing indicator request failed for account {Account} recipient {Recipient} (stop={Stop}).",
                account,
                recipient,
                stop);
        }
    }

    private static bool TryParseSignalMessage(
        JsonElement item,
        out string sender,
        out string text,
        out IReadOnlyList<InboundAttachment> attachments,
        ILogger logger)
    {
        sender = string.Empty;
        text = string.Empty;
        attachments = [];

        if (!item.TryGetProperty("envelope", out var envelope))
        {
            logger.LogDebug("Signal message rejected: payload has no 'envelope' property.");
            return false;
        }

        if (!envelope.TryGetProperty("sourceNumber", out var sourceNumberElement)
            || string.IsNullOrWhiteSpace(sourceNumberElement.GetString()))
        {
            logger.LogWarning("Signal message rejected: 'envelope.sourceNumber' is missing or empty.");
            return false;
        }

        sender = sourceNumberElement.GetString()!;

        JsonElement dataMessage;
        if (envelope.TryGetProperty("dataMessage", out var envelopeDataMessage))
        {
            dataMessage = envelopeDataMessage;
        }
        else if (envelope.TryGetProperty("syncMessage", out var syncMessage)
                 && syncMessage.TryGetProperty("sentMessage", out var sentMessage))
        {
            dataMessage = sentMessage;
        }
        else
        {
            logger.LogWarning("Signal message rejected from {Sender}: no 'dataMessage' or 'syncMessage.sentMessage' in envelope.", sender);
            return false;
        }

        text = dataMessage.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? string.Empty
            : string.Empty;

        attachments = ParseInboundAttachments(dataMessage);

        if (string.IsNullOrWhiteSpace(text) && attachments.Count > 0)
        {
            text = "[non-text Signal message with attachment metadata]";
        }

        if (string.IsNullOrWhiteSpace(text) && attachments.Count == 0)
        {
            logger.LogDebug("Signal message from {Sender} rejected: message text is empty.", sender);
        }

        return !string.IsNullOrWhiteSpace(text) || attachments.Count > 0;
    }

    private static IReadOnlyList<InboundAttachment> ParseInboundAttachments(JsonElement dataMessage)
    {
        if (!dataMessage.TryGetProperty("attachments", out var attachmentsElement)
            || attachmentsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var attachments = new List<InboundAttachment>();

        foreach (var attachment in attachmentsElement.EnumerateArray())
        {
            if (attachment.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var attachmentId = TryReadAttachmentId(attachment);

            var contentType = attachment.TryGetProperty("contentType", out var contentTypeElement)
                ? contentTypeElement.GetString() ?? string.Empty
                : string.Empty;
            var fileName = attachment.TryGetProperty("filename", out var filenameElement)
                ? filenameElement.GetString() ?? string.Empty
                : string.Empty;

            attachments.Add(new InboundAttachment(attachmentId, contentType, fileName, string.Empty, string.Empty));
        }

        return attachments;
    }

    private static string TryReadAttachmentId(JsonElement attachment)
    {
        if (attachment.TryGetProperty("id", out var idElement))
        {
            return ReadAttachmentIdValue(idElement);
        }

        if (attachment.TryGetProperty("attachmentId", out var attachmentIdElement))
        {
            return ReadAttachmentIdValue(attachmentIdElement);
        }

        return string.Empty;
    }

    private static string ReadAttachmentIdValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty
        };

    private static string BuildTracePayload(JsonElement item)
    {
        return TruncateForLog(item.GetRawText());
    }

    private static string TruncateForLog(string value)
    {
        const int maxChars = 4000;
        if (value.Length <= maxChars)
        {
            return value;
        }

        return $"{value[..maxChars]}...(truncated)";
    }

    private async Task<IReadOnlyList<InboundAttachment>> EnrichAttachmentsAsync(
        IReadOnlyList<InboundAttachment> attachments,
        CancellationToken ct)
    {
        if (attachments.Count == 0)
        {
            return attachments;
        }

        var maxImagesPerMessage = Math.Max(0, settings.Value.MaxImageAttachmentsPerMessage);
        var maxAttachmentBytes = settings.Value.MaxImageAttachmentBytes;

        if (maxImagesPerMessage == 0 || maxAttachmentBytes <= 0)
        {
            return attachments;
        }

        var enriched = new List<InboundAttachment>(attachments.Count);
        var imageForwardedCount = 0;

        foreach (var attachment in attachments)
        {
            if (string.IsNullOrWhiteSpace(attachment.AttachmentId))
            {
                enriched.Add(attachment);
                continue;
            }

            var dataUrl = await TryDownloadAttachmentAsync(attachment, maxAttachmentBytes, ct);
            if (string.IsNullOrWhiteSpace(dataUrl))
            {
                enriched.Add(attachment);
                continue;
            }

            if (attachment.IsImage && imageForwardedCount < maxImagesPerMessage)
            {
                enriched.Add(attachment with { ImageDataUrl = dataUrl });
                imageForwardedCount++;
            }
            else if (!attachment.IsImage)
            {
                enriched.Add(attachment with { FileDataUrl = dataUrl });
            }
            else
            {
                enriched.Add(attachment);
            }
        }

        return enriched;
    }

    private async Task<string> TryDownloadAttachmentAsync(InboundAttachment attachment, int maxAttachmentBytes, CancellationToken ct)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient(Constants.HttpClientNames.SignalApi);

            using var response = await httpClient.GetAsync($"/v1/attachments/{Uri.EscapeDataString(attachment.AttachmentId)}", ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "Signal attachment download failed for id {AttachmentId} with status {StatusCode}.",
                    attachment.AttachmentId,
                    response.StatusCode);
                return string.Empty;
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength.HasValue && contentLength.Value > maxAttachmentBytes)
            {
                logger.LogInformation(
                    "Skipping Signal attachment {AttachmentId}: size {SizeBytes} exceeds limit {LimitBytes}.",
                    attachment.AttachmentId,
                    contentLength.Value,
                    maxAttachmentBytes);
                return string.Empty;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            if (bytes.Length > maxAttachmentBytes)
            {
                logger.LogInformation(
                    "Skipping Signal attachment {AttachmentId}: downloaded size {SizeBytes} exceeds limit {LimitBytes}.",
                    attachment.AttachmentId,
                    bytes.Length,
                    maxAttachmentBytes);
                return string.Empty;
            }

            var mediaType = !string.IsNullOrWhiteSpace(attachment.ContentType)
                ? attachment.ContentType
                : response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            var base64 = Convert.ToBase64String(bytes);
            return $"data:{mediaType};base64,{base64}";
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug(ex, "Signal attachment download failed for id {AttachmentId} due to HTTP error.", attachment.AttachmentId);
            return string.Empty;
        }
    }

    private async Task DelayReconnectAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, settings.Value.ReconnectDelaySeconds)), ct);
    }

    private AccountWorkerHealthTracker? GetHealthTracker(string account)
    {
        _workerHealth.TryGetValue(account, out var tracker);
        return tracker;
    }

    private async Task WaitForTaskAsync(Task task, CancellationToken ct)
    {
        try
        {
            await task.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Task was canceled while being awaited during shutdown.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Task faulted while being awaited during shutdown.");
        }
    }

    private sealed class AccountWorkerHealthTracker
    {
        private long _lastSuccessfulReceiveTicks;
        private long _lastWorkerLoopTickTicks;
        private long _lastErrorTicks;
        private int _consecutiveErrors;
        private int _consecutiveSuccesses;
        private int _state = (int)SocketWorkerState.Starting;

        public DateTime StartedUtc { get; } = DateTime.UtcNow;

        public SocketWorkerState State
        {
            get => (SocketWorkerState)Volatile.Read(ref _state);
            set => Volatile.Write(ref _state, (int)value);
        }

        public int ConsecutiveErrors => Volatile.Read(ref _consecutiveErrors);

        public DateTime? LastSuccessfulReceiveUtc => ToNullableUtc(Volatile.Read(ref _lastSuccessfulReceiveTicks));

        public DateTime? LastWorkerLoopTickUtc => ToNullableUtc(Volatile.Read(ref _lastWorkerLoopTickTicks));

        public DateTime? LastErrorUtc => ToNullableUtc(Volatile.Read(ref _lastErrorTicks));

        public void MarkLoopTick()
        {
            Interlocked.Exchange(ref _lastWorkerLoopTickTicks, DateTime.UtcNow.Ticks);
            if (State == SocketWorkerState.Starting)
            {
                State = SocketWorkerState.Running;
            }
        }

        public void MarkRoundTripSuccess(int successesToClearFault)
        {
            var threshold = Math.Max(1, successesToClearFault);
            if (State == SocketWorkerState.Faulted)
            {
                var successes = Interlocked.Increment(ref _consecutiveSuccesses);
                if (successes < threshold)
                {
                    return;
                }
            }

            Interlocked.Exchange(ref _consecutiveSuccesses, 0);
            Interlocked.Exchange(ref _consecutiveErrors, 0);
            State = SocketWorkerState.Running;
        }

        public void MarkSuccessfulReceive()
        {
            Interlocked.Exchange(ref _lastSuccessfulReceiveTicks, DateTime.UtcNow.Ticks);
        }

        public void MarkError()
        {
            Interlocked.Exchange(ref _consecutiveSuccesses, 0);
            Interlocked.Increment(ref _consecutiveErrors);
            Interlocked.Exchange(ref _lastErrorTicks, DateTime.UtcNow.Ticks);
        }

        private static DateTime? ToNullableUtc(long ticks) =>
            ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
    }
}

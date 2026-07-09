using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Channels;

/// <summary>
/// Routes messages received from channels to the agent runtime.
/// </summary>
public sealed class ChannelRouter : IChannelRouter
{
    private readonly IAgentRuntime _runtime;
    private readonly ChannelAuthenticator _authenticator;
    private readonly ISessionStore _sessionStore;
    private readonly ChannelsConfig _channelsConfig;
    private readonly ContinuationProgressConfig _progressConfig;
    private readonly ILogger<ChannelRouter> _logger;
    private readonly ITurnProgressBroker? _progressBroker;
    private readonly ISessionTurnCoordinator? _sessionTurnCoordinator;
    private readonly TimeProvider _timeProvider;
    private readonly IReadOnlyDictionary<string, IChannel> _channels;

    /// <summary>
    /// Parameter object for <see cref="ChannelRouter"/> constructor.
    /// </summary>
    public sealed record ChannelRouterOptions(
        IAgentRuntime Runtime,
        ChannelAuthenticator Authenticator,
        IEnumerable<IChannel> Channels,
        IOptions<ChannelsConfig> ChannelsConfig,
        IOptions<LeanKernelConfig> LeanKernelConfig,
        ISessionStore SessionStore,
        ILogger<ChannelRouter> Logger,
        ITurnProgressBroker? ProgressBroker = null,
        ISessionTurnCoordinator? SessionTurnCoordinator = null,
        TimeProvider? TimeProvider = null);

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelRouter"/> class.
    /// </summary>
    public ChannelRouter(ChannelRouterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _runtime = options.Runtime ?? throw new ArgumentNullException(nameof(options.Runtime));
        _authenticator = options.Authenticator ?? throw new ArgumentNullException(nameof(options.Authenticator));
        _channelsConfig = (options.ChannelsConfig ?? throw new ArgumentNullException(nameof(options.ChannelsConfig))).Value;
        _progressConfig = (options.LeanKernelConfig ?? throw new ArgumentNullException(nameof(options.LeanKernelConfig))).Value.Continuation.Progress;
        _sessionStore = options.SessionStore ?? throw new ArgumentNullException(nameof(options.SessionStore));
        _logger = options.Logger ?? throw new ArgumentNullException(nameof(options.Logger));
        _progressBroker = options.ProgressBroker;
        _sessionTurnCoordinator = options.SessionTurnCoordinator;
        _timeProvider = options.TimeProvider ?? TimeProvider.System;

        var channels = options.Channels ?? throw new ArgumentNullException(nameof(options.Channels));
        var groupedChannels = channels
            .GroupBy(channel => channel.ChannelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var duplicate in groupedChannels.Where(group => group.Count() > 1))
        {
            _logger.LogWarning("Multiple channels were registered for {ChannelId}; the first registration will be used", duplicate.Key);
        }

        _channels = groupedChannels.ToDictionary(
            group => group.Key,
            group => group.First(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelRouter"/> class.
    /// </summary>
    /// <param name="runtime">The agent runtime.</param>
    /// <param name="authenticator">The channel authenticator.</param>
    /// <param name="channels">The collection of available channels.</param>
    /// <param name="config">The channel configuration.</param>
    /// <param name="logger">The logger.</param>
    public ChannelRouter(
        IAgentRuntime runtime,
        ChannelAuthenticator authenticator,
        IEnumerable<IChannel> channels,
        IOptions<ChannelsConfig> channelsConfig,
        IOptions<LeanKernelConfig> leanKernelConfig,
        ISessionStore sessionStore,
        ILogger<ChannelRouter> logger,
        ITurnProgressBroker? progressBroker = null,
        ISessionTurnCoordinator? sessionTurnCoordinator = null,
        TimeProvider? timeProvider = null)
        : this(new ChannelRouterOptions(
            runtime,
            authenticator,
            channels,
            channelsConfig,
            leanKernelConfig,
            sessionStore,
            logger,
            progressBroker,
            sessionTurnCoordinator,
            timeProvider))
    {
    }

    /// <summary>
    /// Routes an inbound channel message to the agent runtime.
    /// </summary>
    /// <param name="message">The message to route.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task RouteInboundAsync(ChannelMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_channelsConfig.Enabled)
        {
            _logger.LogDebug("Skipping inbound channel message because channels are disabled");
            return;
        }

        if (!_channels.TryGetValue(message.ChannelId, out var channel))
        {
            _logger.LogWarning(
                "Unable to route channel message for {ChannelId} from {SenderId}: no channel adapter is registered",
                message.ChannelId,
                message.SenderId);
            return;
        }

        var authorization = _authenticator.Authorize(message);
        if (!authorization.IsAuthorized)
        {
            _logger.LogWarning(
                "Rejected inbound channel message for {ChannelId} from {SenderId}: {Reason}",
                message.ChannelId,
                message.SenderId,
                authorization.Reason);
            return;
        }

        var sessionId = await _sessionStore.GetOrCreateSessionIdAsync(message.ChannelId, message.SenderId, ct).ConfigureAwait(false);
        _sessionTurnCoordinator?.NotifyInbound(sessionId);

        var runtimeMessage = new LeanKernelMessage
        {
            Content = message.Content,
            SenderId = message.SenderId,
            ChannelId = message.ChannelId,
            SessionId = sessionId,
            Timestamp = message.Timestamp,
            Attachments = message.Attachments,
            Metadata = CreateTurnMetadata()
        };

        _logger.LogInformation(
            "Routing inbound channel message for {ChannelId} from {SenderId}",
            message.ChannelId,
            message.SenderId);

        var turnStartedAt = _timeProvider.GetUtcNow();
        var activeTurnId = ResolveTurnId(runtimeMessage.Metadata);
        var progressState = new ProgressState(turnStartedAt, activeTurnId);
        var progressSubscription = _progressBroker?.Subscribe(sessionId, update => HandleProgressUpdateAsync(channel, message.SenderId, progressState, update, ct));
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeatTask = RunHeartbeatLoopAsync(channel, message.SenderId, progressState, heartbeatCts.Token);

        await using var typing = TypingIndicatorKeepAlive.Start(
            channel,
            message.SenderId,
            _channelsConfig.Typing,
            _timeProvider,
            _logger,
            ct);

        try
        {
            AgentResponse response;
            try
            {
                response = await _runtime.RunTurnDetailedAsync(runtimeMessage, ct).ConfigureAwait(false);
            }
            finally
            {
                await FinalizeTurnAsync(progressState, heartbeatCts, progressSubscription, typing, heartbeatTask).ConfigureAwait(false);
            }

            await SendFinalResponseAsync(channel, message.SenderId, response, progressState, ct).ConfigureAwait(false);
        }
        finally
        {
            progressState.Dispose();
        }

        _logger.LogInformation(
            "Delivered channel response for {ChannelId} to {SenderId}",
            message.ChannelId,
            message.SenderId);
    }

    private async Task RunHeartbeatLoopAsync(
        IChannel channel,
        string recipientId,
        ProgressState state,
        CancellationToken ct)
    {
        if (!_progressConfig.Enabled || _progressConfig.HeartbeatSeconds <= 0)
        {
            return;
        }

        var pollPeriod = TimeSpan.FromSeconds(1);
        using var timer = new PeriodicTimer(pollPeriod, _timeProvider);

        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            var now = _timeProvider.GetUtcNow();
            if (now - state.TurnStartedAt < TimeSpan.FromSeconds(Math.Max(0, _progressConfig.InitialSilenceSeconds)))
            {
                continue;
            }

            if (now - state.LastBrokerEventAt < TimeSpan.FromSeconds(Math.Max(1, _progressConfig.HeartbeatSeconds)))
            {
                continue;
            }

            await TrySendProgressAsync(channel, recipientId, state, "⏳ Still working...", TurnProgressKind.Heartbeat, ct).ConfigureAwait(false);
            state.LastBrokerEventAt = now;
        }
    }

    private async Task HandleProgressUpdateAsync(
        IChannel channel,
        string recipientId,
        ProgressState state,
        TurnProgressUpdate update,
        CancellationToken ct)
    {
        if (!state.IsForActiveTurn(update.TurnId))
        {
            _logger.LogDebug(
                "Ignoring progress update for non-active turn {ProgressTurnId}; active turn is {ActiveTurnId}",
                update.TurnId,
                state.ActiveTurnId);
            return;
        }

        state.LastBrokerEventAt = _timeProvider.GetUtcNow();

        string? progressText = update.Kind switch
        {
            TurnProgressKind.ToolStarted when !string.IsNullOrWhiteSpace(update.ToolName) => $"Working with {update.ToolName}...",
            TurnProgressKind.ToolCompleted => update.Message,
            TurnProgressKind.ContinuationStarted => update.Message,
            TurnProgressKind.StatusNote => update.Message,
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(progressText))
        {
            return;
        }

        await TrySendProgressAsync(channel, recipientId, state, progressText, update.Kind, ct).ConfigureAwait(false);
    }

    private async Task TrySendProgressAsync(
        IChannel channel,
        string recipientId,
        ProgressState state,
        string message,
        TurnProgressKind kind,
        CancellationToken ct)
    {
        if (!_progressConfig.Enabled)
        {
            return;
        }

        if (state.IsFinalizing)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        if (now - state.TurnStartedAt < TimeSpan.FromSeconds(Math.Max(0, _progressConfig.InitialSilenceSeconds)))
        {
            return;
        }

        await state.SendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (state.IsFinalizing)
            {
                return;
            }

            now = _timeProvider.GetUtcNow();
            if (now - state.LastSentAt < TimeSpan.FromSeconds(Math.Max(1, _progressConfig.MinIntervalSeconds)))
            {
                return;
            }

            await channel.SendAsync(recipientId, message, ct: ct).ConfigureAwait(false);
            state.LastSentAt = now;
            _logger.LogDebug("Progress update sent ({Kind}) for {ChannelId} -> {RecipientId}", kind, channel.ChannelId, recipientId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to send progress update for {ChannelId} -> {RecipientId}",
                channel.ChannelId,
                recipientId);
        }
        finally
        {
            state.SendGate.Release();
        }
    }

    private async Task SendFinalResponseAsync(
        IChannel channel,
        string recipientId,
        AgentResponse response,
        ProgressState state,
        CancellationToken ct)
    {
        await state.SendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await channel.SendAsync(recipientId, response.Content, response.Attachments, ct).ConfigureAwait(false);
        }
        finally
        {
            state.SendGate.Release();
        }
    }

    private static async Task WaitForHeartbeatAsync(Task heartbeatTask)
    {
        try
        {
            await heartbeatTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task FinalizeTurnAsync(
        ProgressState state,
        CancellationTokenSource heartbeatCts,
        IDisposable? progressSubscription,
        TypingIndicatorKeepAlive typing,
        Task heartbeatTask)
    {
        state.MarkFinalizing();
        heartbeatCts.Cancel();
        progressSubscription?.Dispose();
        await typing.StopAsync().ConfigureAwait(false);
        await WaitForHeartbeatAsync(heartbeatTask).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string> CreateTurnMetadata()
    {
        var turnId = Guid.NewGuid().ToString();
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["turn_id"] = turnId,
            ["turnId"] = turnId,
        };
    }

    private static string ResolveTurnId(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is not null)
        {
            if (metadata.TryGetValue("turn_id", out var turnId) && !string.IsNullOrWhiteSpace(turnId))
            {
                return turnId.Trim();
            }

            if (metadata.TryGetValue("turnId", out turnId) && !string.IsNullOrWhiteSpace(turnId))
            {
                return turnId.Trim();
            }
        }

        return Guid.NewGuid().ToString();
    }

    private sealed class ProgressState(DateTimeOffset turnStartedAt, string activeTurnId) : IDisposable
    {
        private int _isFinalizing;

        public string ActiveTurnId { get; } = activeTurnId;

        public DateTimeOffset TurnStartedAt { get; } = turnStartedAt;

        public DateTimeOffset LastSentAt { get; set; } = DateTimeOffset.MinValue;

        public DateTimeOffset LastBrokerEventAt { get; set; } = turnStartedAt;

        public SemaphoreSlim SendGate { get; } = new(1, 1);

        public bool IsFinalizing => Volatile.Read(ref _isFinalizing) == 1;

        public bool IsForActiveTurn(string? turnId)
            => !string.IsNullOrWhiteSpace(turnId)
                && string.Equals(turnId, ActiveTurnId, StringComparison.Ordinal);

        public void MarkFinalizing()
        {
            Interlocked.Exchange(ref _isFinalizing, 1);
        }

        public void Dispose()
        {
            SendGate.Dispose();
        }
    }
}

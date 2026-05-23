using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Persistence.Resilience;

/// <summary>
/// Falls back to in-memory session storage when PostgreSQL is unavailable.
/// </summary>
public sealed class ResilientSessionStore(
    PostgresSessionStore innerStore,
    DegradedSessionBuffer degradedSessionBuffer,
    ILogger<ResilientSessionStore> logger,
    IProviderHealthTracker? providerHealthTracker = null) : ISessionStore
{
    private readonly PostgresSessionStore _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
    private readonly DegradedSessionBuffer _degradedSessionBuffer = degradedSessionBuffer ?? throw new ArgumentNullException(nameof(degradedSessionBuffer));
    private readonly ILogger<ResilientSessionStore> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IProviderHealthTracker? _providerHealthTracker = providerHealthTracker;

    /// <inheritdoc />
    public async Task<string> GetOrCreateSessionIdAsync(string channelId, string userId, CancellationToken ct = default)
    {
        try
        {
            var sessionId = await _innerStore.GetOrCreateSessionIdAsync(channelId, userId, ct).ConfigureAwait(false);
            _providerHealthTracker?.RecordProbeResult(ProviderNames.Database, ProviderProbeResult.Healthy("Database session lookup succeeded."));
            return sessionId;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _providerHealthTracker?.RecordProbeResult(ProviderNames.Database, ProviderProbeResult.Unhealthy("Database session lookup failed.", ex.Message));
            _logger.LogWarning(ex, "Falling back to in-memory session identifier creation for {ChannelId}/{UserId}", channelId, userId);
            return _degradedSessionBuffer.GetOrCreateSessionId(channelId, userId);
        }
    }

    /// <inheritdoc />
    public async Task AppendTurnAsync(string sessionId, ConversationTurn turn, CancellationToken ct = default)
    {
        try
        {
            await _innerStore.AppendTurnAsync(sessionId, turn, ct).ConfigureAwait(false);
            _providerHealthTracker?.RecordProbeResult(ProviderNames.Database, ProviderProbeResult.Healthy("Database turn persistence succeeded."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _providerHealthTracker?.RecordProbeResult(ProviderNames.Database, ProviderProbeResult.Unhealthy("Database turn persistence failed.", ex.Message));
            _logger.LogWarning(ex, "Persisting turn for session {SessionId} fell back to in-memory storage", sessionId);
            _degradedSessionBuffer.AppendTurn(sessionId, turn);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversationTurn>> GetHistoryAsync(string sessionId, int maxTurns = 50, CancellationToken ct = default)
    {
        try
        {
            var history = await _innerStore.GetHistoryAsync(sessionId, maxTurns, ct).ConfigureAwait(false);
            _providerHealthTracker?.RecordProbeResult(ProviderNames.Database, ProviderProbeResult.Healthy("Database history retrieval succeeded."));
            return history;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _providerHealthTracker?.RecordProbeResult(ProviderNames.Database, ProviderProbeResult.Unhealthy("Database history retrieval failed.", ex.Message));
            _logger.LogWarning(ex, "Retrieving history for session {SessionId} fell back to in-memory storage", sessionId);
            return _degradedSessionBuffer.GetHistory(sessionId, maxTurns);
        }
    }
}

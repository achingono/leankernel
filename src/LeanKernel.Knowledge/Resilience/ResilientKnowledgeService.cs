using System.Collections.Concurrent;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Knowledge.Resilience;

/// <summary>
/// Provides cached, non-throwing knowledge access when GBrain is degraded.
/// </summary>
public sealed class ResilientKnowledgeService(
    GBrainKnowledgeService innerService,
    ILogger<ResilientKnowledgeService> logger,
    IProviderHealthTracker? providerHealthTracker = null) : IKnowledgeService
{
    private readonly GBrainKnowledgeService _innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
    private readonly ILogger<ResilientKnowledgeService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IProviderHealthTracker? _providerHealthTracker = providerHealthTracker;
    private readonly ConcurrentDictionary<string, RetrievalCandidate[]> _searchCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, KnowledgePage> _pageCache = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var cacheKey = CreateSearchCacheKey(query, maxResults);
        if (!IsProviderHealthy() && _searchCache.TryGetValue(cacheKey, out var cachedSearchResults))
        {
            return cachedSearchResults;
        }

        if (!IsProviderHealthy())
        {
            return [];
        }

        try
        {
            var results = await _innerService.SearchAsync(query, maxResults, ct).ConfigureAwait(false);
            var cachedResults = results.ToArray();
            _searchCache[cacheKey] = cachedResults;
            _providerHealthTracker?.RecordProbeResult(ProviderNames.GBrain, ProviderProbeResult.Healthy("GBrain knowledge search succeeded."));
            return cachedResults;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _providerHealthTracker?.RecordProbeResult(ProviderNames.GBrain, ProviderProbeResult.Unhealthy("GBrain knowledge search failed.", ex.Message));
            _logger.LogWarning(ex, "Falling back to cached knowledge search results for query {Query}", query);
            return _searchCache.TryGetValue(cacheKey, out cachedSearchResults)
                ? cachedSearchResults
                : [];
        }
    }

    /// <inheritdoc />
    public async Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!IsProviderHealthy() && _pageCache.TryGetValue(key, out var cachedPage))
        {
            return cachedPage;
        }

        if (!IsProviderHealthy())
        {
            return null;
        }

        try
        {
            var page = await _innerService.GetPageAsync(key, ct).ConfigureAwait(false);
            if (page is not null)
            {
                _pageCache[key] = page;
            }

            _providerHealthTracker?.RecordProbeResult(ProviderNames.GBrain, ProviderProbeResult.Healthy("GBrain page lookup succeeded."));
            return page;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _providerHealthTracker?.RecordProbeResult(ProviderNames.GBrain, ProviderProbeResult.Unhealthy("GBrain page lookup failed.", ex.Message));
            _logger.LogWarning(ex, "Falling back to cached knowledge page for key {Key}", key);
            return _pageCache.TryGetValue(key, out cachedPage)
                ? cachedPage
                : null;
        }
    }

    /// <inheritdoc />
    public async Task PutPageAsync(string key, string content, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);

        if (!IsProviderHealthy())
        {
            _logger.LogWarning("Skipping GBrain page write for key {Key} because the provider is unhealthy", key);
            return;
        }

        try
        {
            await _innerService.PutPageAsync(key, content, ct).ConfigureAwait(false);
            _pageCache[key] = new KnowledgePage
            {
                Key = key,
                Content = content,
                LastModified = DateTimeOffset.UtcNow,
            };
            _providerHealthTracker?.RecordProbeResult(ProviderNames.GBrain, ProviderProbeResult.Healthy("GBrain page write succeeded."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _providerHealthTracker?.RecordProbeResult(ProviderNames.GBrain, ProviderProbeResult.Unhealthy("GBrain page write failed.", ex.Message));
            _logger.LogWarning(ex, "Skipping GBrain page write for key {Key} after provider failure", key);
        }
    }

    /// <inheritdoc />
    public async Task DeletePageAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!IsProviderHealthy())
        {
            _logger.LogWarning("Skipping GBrain page delete for key {Key} because the provider is unhealthy", key);
            _pageCache.TryRemove(key, out _); // Clear stale cache regardless
            return;
        }

        try
        {
            await _innerService.DeletePageAsync(key, ct).ConfigureAwait(false);
            _pageCache.TryRemove(key, out _);
            _providerHealthTracker?.RecordProbeResult(ProviderNames.GBrain, ProviderProbeResult.Healthy("GBrain page delete succeeded."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _providerHealthTracker?.RecordProbeResult(ProviderNames.GBrain, ProviderProbeResult.Unhealthy("GBrain page delete failed.", ex.Message));
            _logger.LogWarning(ex, "Skipping GBrain page delete for key {Key} after provider failure", key);
        }
    }

    private bool IsProviderHealthy()
        => _providerHealthTracker?.GetStatus(ProviderNames.GBrain).IsHealthy ?? true;

    private static string CreateSearchCacheKey(string query, int maxResults)
        => $"{query.Trim()}\u001f{maxResults}";
}

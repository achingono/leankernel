using LeanKernel.Data;
using LeanKernel.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Logic.Providers;

/// <summary>
/// Resolves the effective per-channel memory sharing/isolation policy from persisted
/// <see cref="ChannelMemoryPolicyEntity"/> rows and configuration defaults.
/// </summary>
public sealed class ChannelMemoryPolicyResolver : IChannelMemoryPolicyResolver
{
    private readonly IDbContextFactory<EntityContext> _contextFactory;
    private readonly ILogger<ChannelMemoryPolicyResolver> _logger;

    public ChannelMemoryPolicyResolver(
        IDbContextFactory<EntityContext> contextFactory,
        ILogger<ChannelMemoryPolicyResolver> logger)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetReadableChannelsAsync(
        Guid tenantId,
        string channelName,
        CancellationToken ct = default)
    {
        var allChannels = await GetAllChannelNamesAsync(tenantId, ct).ConfigureAwait(false);
        var policies = await GetPoliciesAsync(tenantId, ct).ConfigureAwait(false);

        var readable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { channelName };

        foreach (var otherChannel in allChannels)
        {
            if (string.Equals(otherChannel, channelName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsReadableBy(otherChannel, channelName, policies))
            {
                readable.Add(otherChannel);
            }
        }

        _logger.LogDebug(
            "Resolved readable channels for {Channel} in tenant {Tenant}: {Count} channels",
            channelName, tenantId, readable.Count);

        return readable;
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetMutuallyVisibleChannelsAsync(
        Guid tenantId,
        string channelName,
        CancellationToken ct = default)
    {
        var allChannels = await GetAllChannelNamesAsync(tenantId, ct).ConfigureAwait(false);
        var policies = await GetPoliciesAsync(tenantId, ct).ConfigureAwait(false);

        var mutuallyVisible = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { channelName };

        foreach (var otherChannel in allChannels)
        {
            if (string.Equals(otherChannel, channelName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Mutual visibility: other shares to channel AND channel shares to other
            var otherSharesToMe = IsReadableBy(otherChannel, channelName, policies);
            var iShareToOther = IsReadableBy(channelName, otherChannel, policies);

            if (otherSharesToMe && iShareToOther)
            {
                mutuallyVisible.Add(otherChannel);
            }
        }

        _logger.LogDebug(
            "Resolved mutually visible channels for {Channel} in tenant {Tenant}: {Count} channels",
            channelName, tenantId, mutuallyVisible.Count);

        return mutuallyVisible;
    }

    /// <inheritdoc />
    public Task SignalPolicyChangedAsync(
        Guid tenantId,
        string channelName,
        CancellationToken ct = default)
    {
        // Placeholder for Phase 10 integration: will trigger fact reconciliation.
        _logger.LogInformation(
            "Memory sharing policy changed for {Channel} in tenant {Tenant} — reconciliation signal emitted",
            channelName, tenantId);
        return Task.CompletedTask;
    }

    private static bool IsReadableBy(
        string sourceChannel,
        string readerChannel,
        IReadOnlyDictionary<string, ChannelMemoryPolicyEntity> policies)
    {
        if (string.Equals(sourceChannel, readerChannel, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Directional AND: source shares to reader AND reader accesses source.
        var sourcePolicy = GetPolicyOrWildcardDefault(sourceChannel, policies);
        var readerPolicy = GetPolicyOrWildcardDefault(readerChannel, policies);

        var sharesToReader = MatchesWildcardList(sourcePolicy.Share, readerChannel);
        var readerAccessesSource = MatchesWildcardList(readerPolicy.Access, sourceChannel);

        return sharesToReader && readerAccessesSource;
    }

    private static ChannelMemoryPolicyEntity GetPolicyOrWildcardDefault(
        string channelName,
        IReadOnlyDictionary<string, ChannelMemoryPolicyEntity> policies)
    {
        if (policies.TryGetValue(channelName, out var persisted))
        {
            return persisted;
        }

        return new ChannelMemoryPolicyEntity
        {
            Share = ChannelMemoryPolicyEntity.Wildcard,
            Access = ChannelMemoryPolicyEntity.Wildcard,
        };
    }

    private static bool MatchesWildcardList(string csvList, string channelName)
    {
        if (string.IsNullOrWhiteSpace(csvList))
        {
            return false;
        }

        foreach (var entry in csvList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(entry, ChannelMemoryPolicyEntity.Wildcard, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(entry, channelName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<IReadOnlyList<string>> GetAllChannelNamesAsync(Guid tenantId, CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await context.Channels
            .Select(c => c.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, ChannelMemoryPolicyEntity>> GetPoliciesAsync(
        Guid tenantId,
        CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var policies = await context.Set<ChannelMemoryPolicyEntity>()
            .Where(p => p.TenantId == tenantId)
            .Include(p => p.Channel)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return policies
            .Where(p => p.Channel is not null)
            .ToDictionary(
                p => p.Channel!.Name,
                p => p,
                StringComparer.OrdinalIgnoreCase);
    }
}

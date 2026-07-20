using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Providers;

/// <summary>
/// Resolves directional channel memory visibility from defaults plus persisted tenant overrides.
/// </summary>
public sealed class ChannelMemoryPolicyResolver(
    IDbContextFactory<EntityContext> dbContextFactory,
    IOptions<AgentSettings> agentSettings) : IChannelMemoryPolicyResolver
{
    /// <inheritdoc />
    public async Task<ChannelMemoryPolicyResolution> ResolveAsync(Guid tenantId, Guid channelId, CancellationToken ct = default)
    {
        using var context = await dbContextFactory.CreateDbContextAsync(ct);

        var channels = await context.Channels
            .AsNoTracking()
            .ToListAsync(ct);

        if (!channels.Any(c => c.Id == channelId))
        {
            return new ChannelMemoryPolicyResolution
            {
                TenantId = tenantId,
                ChannelId = channelId,
                ReadableChannelIds = [channelId],
                MutuallyVisibleChannelIds = [channelId]
            };
        }

        var overrideRows = await context.ChannelMemoryPolicies
            .AsNoTracking()
            .Where(policy => policy.TenantId == tenantId)
            .ToListAsync(ct);

        var defaults = agentSettings.Value.Channels.MemoryPolicyDefaults;
        var defaultShare = NormalizeList(defaults.Share);
        var defaultAccess = NormalizeList(defaults.Access);

        var channelById = channels.ToDictionary(c => c.Id);
        var policies = channels.ToDictionary(
            c => c.Id,
            c =>
            {
                var row = overrideRows.FirstOrDefault(overrideRow => overrideRow.ChannelId == c.Id);
                return new Policy(
                    row is null ? defaultShare : NormalizeList(ParseList(row.ShareList)),
                    row is null ? defaultAccess : NormalizeList(ParseList(row.AccessList)));
            });

        bool IsReadable(Guid sourceChannelId, Guid candidateChannelId)
        {
            if (sourceChannelId == candidateChannelId)
            {
                return true;
            }

            var source = channelById[sourceChannelId];
            var candidate = channelById[candidateChannelId];
            var sourcePolicy = policies[sourceChannelId];
            var candidatePolicy = policies[candidateChannelId];

            var sourceCanAccess = sourcePolicy.Access.Contains(ChannelEntity.MemoryPolicyWildcard)
                                  || sourcePolicy.Access.Contains(candidate.Name);
            var candidateSharesToSource = candidatePolicy.Share.Contains(ChannelEntity.MemoryPolicyWildcard)
                                          || candidatePolicy.Share.Contains(source.Name);
            return sourceCanAccess && candidateSharesToSource;
        }

        var readable = channels
            .Where(channel => IsReadable(channelId, channel.Id))
            .Select(channel => channel.Id)
            .Distinct()
            .ToArray();

        var mutuallyVisible = channels
            .Where(channel => IsReadable(channelId, channel.Id) && IsReadable(channel.Id, channelId))
            .Select(channel => channel.Id)
            .Distinct()
            .ToArray();

        return new ChannelMemoryPolicyResolution
        {
            TenantId = tenantId,
            ChannelId = channelId,
            ReadableChannelIds = readable,
            MutuallyVisibleChannelIds = mutuallyVisible
        };
    }

    private static IReadOnlySet<string> NormalizeList(IEnumerable<string> source)
    {
        var normalized = source
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (normalized.Contains(ChannelEntity.MemoryPolicyWildcard))
        {
            return new HashSet<string>([ChannelEntity.MemoryPolicyWildcard], StringComparer.OrdinalIgnoreCase);
        }

        return normalized;
    }

    private static IEnumerable<string> ParseList(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private sealed record Policy(IReadOnlySet<string> Share, IReadOnlySet<string> Access);
}
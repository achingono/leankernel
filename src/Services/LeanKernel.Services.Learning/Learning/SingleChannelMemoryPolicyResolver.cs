using LeanKernel.Entities;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Resolves memory policy to the current channel only.
/// </summary>
public sealed class SingleChannelMemoryPolicyResolver : IChannelMemoryPolicyResolver
{
    /// <inheritdoc />
    public Task<ChannelMemoryPolicyResolution> ResolveAsync(Guid tenantId, Guid channelId, CancellationToken ct = default)
    {
        return Task.FromResult(new ChannelMemoryPolicyResolution
        {
            TenantId = tenantId,
            ChannelId = channelId,
            ReadableChannelIds = [channelId],
            MutuallyVisibleChannelIds = [channelId]
        });
    }
}

namespace LeanKernel.Entities;

/// <summary>
/// Resolves tenant-scoped channel memory visibility according to directional share/access policy.
/// </summary>
public interface IChannelMemoryPolicyResolver
{
    /// <summary>
    /// Resolves the effective readable and mutually-visible channel sets for a source channel.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="channelId">The source channel identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChannelMemoryPolicyResolution> ResolveAsync(Guid tenantId, Guid channelId, CancellationToken ct = default);
}
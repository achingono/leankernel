namespace LeanKernel.Entities;

/// <summary>
/// Represents resolved channel-memory visibility for a single channel in a tenant.
/// </summary>
public sealed class ChannelMemoryPolicyResolution
{
    /// <summary>
    /// Gets or sets the tenant id used to resolve policy.
    /// </summary>
    public required Guid TenantId { get; init; }

    /// <summary>
    /// Gets or sets the source channel id for this resolution.
    /// </summary>
    public required Guid ChannelId { get; init; }

    /// <summary>
    /// Gets or sets channel ids that this source channel may read from.
    /// </summary>
    public required IReadOnlyCollection<Guid> ReadableChannelIds { get; init; }

    /// <summary>
    /// Gets or sets channel ids that are mutually visible with the source channel.
    /// </summary>
    public required IReadOnlyCollection<Guid> MutuallyVisibleChannelIds { get; init; }
}
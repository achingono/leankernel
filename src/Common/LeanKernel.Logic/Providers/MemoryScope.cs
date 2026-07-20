namespace LeanKernel.Logic.Providers;

/// <summary>
/// Represents the scope within which memories are stored and retrieved.
/// </summary>
public sealed class MemoryScope
{
    /// <summary>
    /// Gets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; init; }

    /// <summary>
    /// Gets the canonical person identifier.
    /// </summary>
    public Guid PersonId { get; init; }

    /// <summary>
    /// Gets the channel identifier.
    /// </summary>
    public Guid ChannelId { get; init; }

    /// <summary>
    /// Gets the optional explicit channel set used for read fan-out.
    /// When null, the memory client resolves the readable set from policy.
    /// </summary>
    public IReadOnlyCollection<Guid>? SearchChannelIds { get; init; }
}

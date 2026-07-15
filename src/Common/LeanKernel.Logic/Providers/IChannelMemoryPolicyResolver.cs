namespace LeanKernel.Logic.Providers;

/// <summary>
/// Resolves the effective per-channel memory sharing/isolation policy.
/// Directional AND: channel X's memory is readable in channel C iff
/// X shares to C AND C accesses X. A channel always reads itself.
/// </summary>
public interface IChannelMemoryPolicyResolver
{
    /// <summary>
    /// Returns the set of channel names whose memory the given channel may read.
    /// Uses directional AND semantics: X is readable in C iff X shares to C and C accesses X.
    /// The channel itself is always included.
    /// </summary>
    /// <param name="tenantId">The tenant scope.</param>
    /// <param name="channelName">The channel whose read set is being resolved.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The set of readable channel names (including the channel itself).</returns>
    Task<IReadOnlySet<string>> GetReadableChannelsAsync(
        Guid tenantId,
        string channelName,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the set of channels that are mutually visible to the given channel
    /// (both directions share and access). This is the boundary Phase 10 uses for 5W1H fact reconciliation.
    /// </summary>
    /// <param name="tenantId">The tenant scope.</param>
    /// <param name="channelName">The channel whose mutual set is being resolved.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The set of mutually visible channel names (including the channel itself).</returns>
    Task<IReadOnlySet<string>> GetMutuallyVisibleChannelsAsync(
        Guid tenantId,
        string channelName,
        CancellationToken ct = default);

    /// <summary>
    /// Signals that the sharing policy has changed so downstream systems (Phase 10) can reconcile facts.
    /// </summary>
    /// <param name="tenantId">The tenant scope.</param>
    /// <param name="channelName">The channel whose policy changed.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SignalPolicyChangedAsync(
        Guid tenantId,
        string channelName,
        CancellationToken ct = default);
}

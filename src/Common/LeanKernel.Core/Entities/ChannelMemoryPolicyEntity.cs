namespace LeanKernel.Entities;

/// <summary>
/// Stores the per-channel memory sharing/isolation policy.
/// Two directional allow-lists: <see cref="Share"/> (which channels may read this channel's memory)
/// and <see cref="Access"/> (which channels' memory this channel may read).
/// Each list accepts explicit channel names or the wildcard <c>*</c>.
/// </summary>
public sealed class ChannelMemoryPolicyEntity : IEntity
{
    /// <summary>
    /// Gets or sets the unique policy identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the tenant this policy belongs to.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the channel this policy belongs to.
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the navigation property for the channel.
    /// </summary>
    public ChannelEntity? Channel { get; set; }

    /// <summary>
    /// Gets or sets the comma-separated list of channel names that may read this channel's memory.
    /// <c>*</c> means all channels. Default is <c>*</c>.
    /// </summary>
    public string Share { get; set; } = Wildcard;

    /// <summary>
    /// Gets or sets the comma-separated list of channel names whose memory this channel may read.
    /// <c>*</c> means all channels. Default is <c>*</c>.
    /// </summary>
    public string Access { get; set; } = Wildcard;

    /// <summary>
    /// The wildcard constant representing full sharing/access.
    /// </summary>
    public const string Wildcard = "*";
}

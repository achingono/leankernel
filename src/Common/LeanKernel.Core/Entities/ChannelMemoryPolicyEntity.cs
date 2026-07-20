namespace LeanKernel.Entities;

/// <summary>
/// Represents a tenant-scoped per-channel memory sharing policy override.
/// </summary>
public class ChannelMemoryPolicyEntity : IEntity
{
    /// <summary>
    /// Gets or sets the unique policy identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the tenant id for this override.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the channel id this override applies to.
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets a comma-separated allow-list of channel names this channel shares to.
    /// </summary>
    public string ShareList { get; set; } = ChannelEntity.MemoryPolicyWildcard;

    /// <summary>
    /// Gets or sets a comma-separated allow-list of channel names this channel can access.
    /// </summary>
    public string AccessList { get; set; } = ChannelEntity.MemoryPolicyWildcard;

    /// <summary>
    /// Gets or sets when this override was created.
    /// </summary>
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets when this override was last updated.
    /// </summary>
    public DateTime? UpdatedOn { get; set; }

    /// <summary>
    /// Gets or sets the tenant navigation.
    /// </summary>
    public virtual TenantEntity Tenant { get; set; } = default!;

    /// <summary>
    /// Gets or sets the channel navigation.
    /// </summary>
    public virtual ChannelEntity Channel { get; set; } = default!;
}
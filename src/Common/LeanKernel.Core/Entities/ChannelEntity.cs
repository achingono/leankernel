namespace LeanKernel.Entities;

/// <summary>
/// Represents a communication channel (e.g., OpenAI HTTP surface, Teams, Slack).
/// </summary>
public class ChannelEntity : IEntity
{
    /// <summary>
    /// Gets or sets the unique channel identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the channel display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets sender bindings configured for this channel.
    /// </summary>
    public virtual ICollection<ChannelSenderBindingEntity> SenderBindings { get; set; } = new List<ChannelSenderBindingEntity>();

    /// <summary>
    /// Gets or sets tenant-level memory policy overrides for this channel.
    /// </summary>
    public virtual ICollection<ChannelMemoryPolicyEntity> MemoryPolicies { get; set; } = new List<ChannelMemoryPolicyEntity>();

    /// <summary>
    /// Gets or sets the sessions associated with this channel.
    /// </summary>
    public virtual ICollection<SessionEntity> Sessions { get; set; } = new List<SessionEntity>();
}
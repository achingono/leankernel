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

    /// <summary>
    /// Well-known channel name for the OpenAI-compatible HTTP surface.
    /// </summary>
    public const string OpenAiHttpName = "openai-http";

    /// <summary>
    /// Well-known channel name for the Signal terminal.
    /// </summary>
    public const string SignalName = "signal";

    /// <summary>
    /// Well-known channel name for the Microsoft Teams terminal.
    /// </summary>
    public const string TeamsName = "teams";

    /// <summary>
    /// Wildcard token used by channel memory policy allow-lists.
    /// </summary>
    public const string MemoryPolicyWildcard = "*";
}
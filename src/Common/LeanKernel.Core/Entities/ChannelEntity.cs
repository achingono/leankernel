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
    /// Gets or sets the sessions associated with this channel.
    /// </summary>
    public ICollection<SessionEntity> Sessions { get; set; } = new List<SessionEntity>();

    /// <summary>
    /// Well-known channel name for the OpenAI-compatible HTTP surface.
    /// </summary>
    public const string OpenAiHttpName = "openai-http";
}

namespace LeanKernel.Channels.Teams.Models;

/// <summary>Represents an incoming activity from the Teams Bot Framework webhook.</summary>
public sealed class IncomingActivity
{
    /// <summary>Gets or sets the activity identifier.</summary>
    public string? Id { get; set; }
    /// <summary>Gets or sets the activity type.</summary>
    public string? Type { get; set; }
    /// <summary>Gets or sets the message text.</summary>
    public string? Text { get; set; }
    /// <summary>Gets or sets the service URL.</summary>
    public string? ServiceUrl { get; set; }
    /// <summary>Gets or sets the sender actor.</summary>
    public Actor? From { get; set; }
    /// <summary>Gets or sets the conversation.</summary>
    public Conversation? Conversation { get; set; }
    /// <summary>Gets or sets the message attachments.</summary>
    public List<Attachment>? Attachments { get; set; }
}
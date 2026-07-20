namespace LeanKernel.Services.Common.Contracts;

/// <summary>
/// Represents one message within a completed turn.
/// </summary>
public sealed record TurnMessage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TurnMessage"/> record.
    /// </summary>
    public TurnMessage(string role, string text, DateTimeOffset? createdAt = null, string? authorName = null)
    {
        Role = role;
        Text = text;
        CreatedAt = createdAt;
        AuthorName = authorName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TurnMessage"/> record.
    /// </summary>
    public TurnMessage()
    {
    }

    /// <summary>
    /// Gets or sets the role (for example, user or assistant).
    /// </summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the message text.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional message creation timestamp.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Gets or sets the optional author display name.
    /// </summary>
    public string? AuthorName { get; init; }
}

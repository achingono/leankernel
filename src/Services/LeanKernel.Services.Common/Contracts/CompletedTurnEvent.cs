namespace LeanKernel.Services.Common.Contracts;

/// <summary>
/// Represents a completed conversational turn published to the learning pipeline.
/// </summary>
public sealed record CompletedTurnEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompletedTurnEvent"/> record.
    /// </summary>
    public CompletedTurnEvent(
        Guid tenantId,
        Guid userId,
        Guid personId,
        Guid channelId,
        string? sessionId,
        string turnId,
        DateTimeOffset recordedAt,
        IReadOnlyList<TurnMessage> requestMessages,
        IReadOnlyList<TurnMessage> responseMessages)
    {
        TenantId = tenantId;
        UserId = userId;
        PersonId = personId;
        ChannelId = channelId;
        SessionId = sessionId;
        TurnId = turnId;
        RecordedAt = recordedAt;
        RequestMessages = requestMessages;
        ResponseMessages = responseMessages;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompletedTurnEvent"/> record.
    /// </summary>
    public CompletedTurnEvent()
    {
    }

    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; init; }

    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets or sets the canonical person identifier.
    /// </summary>
    public Guid PersonId { get; init; }

    /// <summary>
    /// Gets or sets the channel identifier.
    /// </summary>
    public Guid ChannelId { get; init; }

    /// <summary>
    /// Gets or sets the optional session identifier.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets or sets the turn identifier.
    /// </summary>
    public string TurnId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC timestamp when the turn was recorded.
    /// </summary>
    public DateTimeOffset RecordedAt { get; init; }

    /// <summary>
    /// Gets or sets request messages authored during the turn.
    /// </summary>
    public IReadOnlyList<TurnMessage> RequestMessages { get; init; } = [];

    /// <summary>
    /// Gets or sets response messages produced during the turn.
    /// </summary>
    public IReadOnlyList<TurnMessage> ResponseMessages { get; init; } = [];
}

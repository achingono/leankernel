using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.Resources;

namespace LeanKernel.Thinker;

/// <summary>
/// Completes the durable post-turn work that must happen after a response is produced.
/// </summary>
public sealed class PostTurnPipeline
{
    private readonly ISessionStore _sessions;
    private readonly ITurnEventSink? _turnEventSink;
    private readonly ILogger<PostTurnPipeline> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostTurnPipeline" /> class.
    /// </summary>
    /// <param name="sessions">The session store used to persist assistant turns.</param>
    /// <param name="logger">The logger used for post-turn diagnostics.</param>
    /// <param name="turnEventSink">The optional sink for durable self-improvement events.</param>
    public PostTurnPipeline(
        ISessionStore sessions,
        ILogger<PostTurnPipeline> logger,
        ITurnEventSink? turnEventSink = null)
    {
        _sessions = sessions;
        _logger = logger;
        _turnEventSink = turnEventSink;
    }

    /// <summary>
    /// Persists the assistant response and publishes the learning event for a completed turn.
    /// </summary>
    /// <param name="sessionId">The conversation session identifier.</param>
    /// <param name="message">The inbound user message.</param>
    /// <param name="response">The assistant response text.</param>
    /// <param name="context">The gated context used for the turn.</param>
    /// <param name="errorType">The optional error type captured during turn execution.</param>
    /// <param name="errorMessage">The optional error message captured during turn execution.</param>
    /// <param name="ct">A token used to cancel post-turn persistence.</param>
    /// <returns>A task that completes when required post-turn work is durable.</returns>
    public async Task CompleteAsync(
        string sessionId,
        LeanKernelMessage message,
        string response,
        ConversationContext context,
        string? errorType,
        string? errorMessage,
        CancellationToken ct)
    {
        await _sessions.AppendTurnAsync(sessionId, new ConversationTurn
        {
            Role = "assistant",
            Content = response,
            Timestamp = DateTimeOffset.UtcNow
        }, ct);

        if (_turnEventSink is null)
        {
            return;
        }

        try
        {
            await _turnEventSink.EnqueueAsync(new TurnEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                SessionId = sessionId,
                UserMessage = message,
                AssistantResponse = response,
                Context = context,
                SourceId = $"conversation:{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ss}",
                CompletedAt = DateTimeOffset.UtcNow,
                ErrorType = errorType,
                ErrorMessage = errorMessage
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ResourceText.Log("TurnEventEnqueueFailed"));
        }
    }
}

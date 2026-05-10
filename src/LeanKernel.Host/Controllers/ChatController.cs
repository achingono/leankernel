using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Host.Services;
using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Host.Controllers;

/// <summary>
/// Represents the chat controller.
/// </summary>
[ApiController]
[Route("api/chat")]
[Authorize(Policy = AuthConstants.PolicyAdminOnly)]
public sealed class ChatController : ControllerBase
{
    private readonly ISessionStore _sessions;
    private readonly IThinkerService _thinker;
    private readonly IMessageQueue _messageQueue;
    private readonly ITimeBoundaryService _timeBoundary;
    private readonly InboundAttachmentInputProcessor _attachmentProcessor;

    /// <summary>
    /// Represents the chat controller.
    /// </summary>
    public ChatController(
        ISessionStore sessions,
        IThinkerService thinker,
        IMessageQueue messageQueue,
        ITimeBoundaryService timeBoundary,
        InboundAttachmentInputProcessor attachmentProcessor)
    {
        _sessions = sessions;
        _thinker = thinker;
        _messageQueue = messageQueue;
        _timeBoundary = timeBoundary;
        _attachmentProcessor = attachmentProcessor;
    }

    /// <summary>
    /// Executes the list sessions operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    [HttpGet("sessions")]
    public async Task<IActionResult> ListSessions(CancellationToken ct)
    {
        var sessions = await _sessions.ListSessionsAsync(ct);
        return Ok(sessions);
    }

    /// <summary>
    /// Executes the get session operation.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetSession(string sessionId, CancellationToken ct)
    {
        var history = await _sessions.GetHistoryAsync(sessionId, ct);
        return Ok(new { sessionId, turns = history });
    }

    /// <summary>
    /// Represents the send message.
    /// </summary>
    [HttpPost("message")]
    public async Task<IActionResult> SendMessage(
        [FromBody] ChatMessageRequest request,
        CancellationToken ct)
    {
        IReadOnlyList<InboundAttachment> attachments;
        try
        {
            attachments = await _attachmentProcessor.ProcessAsync(request.Attachments, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var message = new LeanKernelMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            ChannelId = "web",
            SenderId = request.SenderId ?? "web-user",
            Content = InboundMessageContentFormatter.FormatContent(request.Content, attachments),
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = InboundMessageContentFormatter.BuildMetadata("web", attachments)
        };

        // Check if we're in quiet hours
        var status = _timeBoundary.GetStatus();
        if (status.IsQuietHours)
        {
            // Queue the message instead of processing immediately
            var isUrgent = request.IsUrgent ?? false;
            await _messageQueue.EnqueueAsync(new QueuedMessage
            {
                Id = message.Id,
                Channel = message.ChannelId,
                Recipient = message.SenderId,
                Content = message.Content,
                EnqueuedAt = DateTime.UtcNow,
                Priority = isUrgent ? 1 : 5
            }, isUrgent, ct);

            return Accepted(new ChatMessageResponse
            {
                MessageId = message.Id,
                Response = "Message queued for processing during active hours",
                Timestamp = DateTimeOffset.UtcNow,
                Queued = true
            });
        }

        var response = await _thinker.ProcessAsync(message, ct);

        return Ok(new ChatMessageResponse
        {
            MessageId = message.Id,
            Response = response,
            Timestamp = DateTimeOffset.UtcNow,
            Queued = false
        });
    }

    /// <summary>
    /// Represents the deliver via channel.
    /// </summary>
    [HttpPost("deliver/{channel}")]
    public async Task<IActionResult> DeliverViaChannel(
        string channel,
        [FromBody] ChannelDeliveryRequest request,
        CancellationToken ct)
    {
        var messageId = Guid.NewGuid().ToString("N");

        // Queue the message for delivery
        await _messageQueue.EnqueueAsync(new QueuedMessage
        {
            Id = messageId,
            Channel = channel,
            Recipient = request.Recipient,
            Content = request.Content,
            EnqueuedAt = DateTime.UtcNow,
            Priority = request.IsUrgent ? 1 : 5
        }, request.IsUrgent, ct);

        return Accepted(new
        {
            messageId,
            channel,
            recipient = request.Recipient,
            queued = true,
            message = "Message queued for delivery"
        });
    }
}

/// <summary>
/// Represents the chat message request.
/// </summary>
public sealed class ChatMessageRequest
{
    /// <summary>
    /// Gets or sets the content.
    /// </summary>
    public required string Content { get; init; }
    /// <summary>
    /// Gets or sets the sender id.
    /// </summary>
    public string? SenderId { get; init; }
    /// <summary>
    /// Gets or sets the session id.
    /// </summary>
    public string? SessionId { get; init; }
    /// <summary>
    /// Gets or sets the is urgent.
    /// </summary>
    public bool? IsUrgent { get; init; }
    /// <summary>
    /// Gets or sets the attachments.
    /// </summary>
    public List<InboundAttachmentInput>? Attachments { get; init; }
}

/// <summary>
/// Represents the chat message response.
/// </summary>
public sealed class ChatMessageResponse
{
    /// <summary>
    /// Gets or sets the message id.
    /// </summary>
    public required string MessageId { get; init; }
    /// <summary>
    /// Gets or sets the response.
    /// </summary>
    public required string Response { get; init; }
    /// <summary>
    /// Gets or sets the timestamp.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
    /// <summary>
    /// Gets or sets the queued.
    /// </summary>
    public bool Queued { get; init; }
}

/// <summary>
/// Represents the channel delivery request.
/// </summary>
public sealed class ChannelDeliveryRequest
{
    /// <summary>
    /// Gets or sets the recipient.
    /// </summary>
    public required string Recipient { get; init; }
    /// <summary>
    /// Gets or sets the content.
    /// </summary>
    public required string Content { get; init; }
    /// <summary>
    /// Gets or sets the is urgent.
    /// </summary>
    public bool IsUrgent { get; init; } = false;
}

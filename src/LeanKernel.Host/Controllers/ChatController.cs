using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Host.Services;
using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Host.Controllers;

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

    [HttpGet("sessions")]
    public async Task<IActionResult> ListSessions(CancellationToken ct)
    {
        var sessions = await _sessions.ListSessionsAsync(ct);
        return Ok(sessions);
    }

    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetSession(string sessionId, CancellationToken ct)
    {
        var history = await _sessions.GetHistoryAsync(sessionId, ct);
        return Ok(new { sessionId, turns = history });
    }

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

public sealed class ChatMessageRequest
{
    public required string Content { get; init; }
    public string? SenderId { get; init; }
    public string? SessionId { get; init; }
    public bool? IsUrgent { get; init; }
    public List<InboundAttachmentInput>? Attachments { get; init; }
}

public sealed class ChatMessageResponse
{
    public required string MessageId { get; init; }
    public required string Response { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public bool Queued { get; init; }
}

public sealed class ChannelDeliveryRequest
{
    public required string Recipient { get; init; }
    public required string Content { get; init; }
    public bool IsUrgent { get; init; } = false;
}

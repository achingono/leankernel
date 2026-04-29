using Microsoft.AspNetCore.Mvc;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Host.Controllers;

[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly ISessionStore _sessions;
    private readonly IThinkerService _thinker;

    public ChatController(ISessionStore sessions, IThinkerService thinker)
    {
        _sessions = sessions;
        _thinker = thinker;
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
        var message = new LeanKernelMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            ChannelId = "web",
            SenderId = request.SenderId ?? "web-user",
            Content = request.Content,
            Timestamp = DateTimeOffset.UtcNow
        };

        var response = await _thinker.ProcessAsync(message, ct);

        return Ok(new ChatMessageResponse
        {
            MessageId = message.Id,
            Response = response,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}

public sealed class ChatMessageRequest
{
    public required string Content { get; init; }
    public string? SenderId { get; init; }
    public string? SessionId { get; init; }
}

public sealed class ChatMessageResponse
{
    public required string MessageId { get; init; }
    public required string Response { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Host.Services.Auth;
using LeanKernel.Host.Services;

namespace LeanKernel.Host.Controllers;

/// <summary>
/// OpenAI-compatible API endpoints. Allows any OpenAI SDK client to
/// connect to LeanKernel as a model provider.
/// </summary>
[ApiController]
[Route("v1")]
[Authorize(Policy = AuthConstants.PolicyApiAccess)]
public sealed class OpenAiController : ControllerBase
{
    private readonly IThinkerService _thinker;
    private readonly ILogger<OpenAiController> _logger;
    private readonly InboundAttachmentInputProcessor _attachmentProcessor;

    public OpenAiController(
        IThinkerService thinker,
        ILogger<OpenAiController> logger,
        InboundAttachmentInputProcessor attachmentProcessor)
    {
        _thinker = thinker;
        _logger = logger;
        _attachmentProcessor = attachmentProcessor;
    }

    /// <summary>
    /// POST /v1/chat/completions — standard OpenAI chat completion format.
    /// </summary>
    [HttpPost("chat/completions")]
    public async Task<IActionResult> ChatCompletions(
        [FromBody] OpenAiChatRequest request,
        CancellationToken ct)
    {
        _logger.LogInformation("OpenAI-compat request: model={Model}, messages={Count}",
            request.Model, request.Messages?.Count ?? 0);

        var lastUserMessage = request.Messages?
            .LastOrDefault(m => m.Role == "user");

        IReadOnlyList<InboundAttachment> attachments;
        try
        {
            attachments = await _attachmentProcessor.ProcessAsync(lastUserMessage?.Attachments, ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var formattedPrompt = InboundMessageContentFormatter.FormatContent(
            lastUserMessage?.Content ?? string.Empty,
            attachments);

        var message = new LeanKernelMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            ChannelId = "openai-api",
            SenderId = request.User ?? "api-client",
            Content = formattedPrompt,
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = InboundMessageContentFormatter.BuildMetadata("openai-api", attachments)
        };

        var response = await _thinker.ProcessAsync(message, ct);
        var completionId = $"chatcmpl-{Guid.NewGuid():N}"[..29];

        return Ok(new OpenAiChatResponse
        {
            Id = completionId,
            Object = "chat.completion",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = request.Model ?? "LeanKernel",
            Choices =
            [
                new OpenAiChoice
                {
                    Index = 0,
                    Message = new OpenAiMessage { Role = "assistant", Content = response },
                    FinishReason = "stop"
                }
            ],
            Usage = new OpenAiUsage
            {
                PromptTokens = EstimateTokens(formattedPrompt),
                CompletionTokens = EstimateTokens(response),
                TotalTokens = EstimateTokens(formattedPrompt) + EstimateTokens(response)
            }
        });
    }

    /// <summary>
    /// GET /v1/models — list available models.
    /// </summary>
    [HttpGet("models")]
    public IActionResult ListModels()
    {
        return Ok(new
        {
            @object = "list",
            data = new[]
            {
                new
                {
                    id = "LeanKernel",
                    @object = "model",
                    created = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
                    owned_by = "LeanKernel-local"
                }
            }
        });
    }

    private static int EstimateTokens(string text) => text.Length / 4;
}

// ── OpenAI Protocol Types ────────────────────────────────────────

public sealed class OpenAiChatRequest
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("messages")]
    public List<OpenAiMessage>? Messages { get; init; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    [JsonPropertyName("user")]
    public string? User { get; init; }
}

public sealed class OpenAiMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }

    [JsonPropertyName("attachments")]
    public List<InboundAttachmentInput>? Attachments { get; init; }
}

public sealed class OpenAiChatResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("object")]
    public required string Object { get; init; }

    [JsonPropertyName("created")]
    public long Created { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("choices")]
    public required List<OpenAiChoice> Choices { get; init; }

    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; init; }
}

public sealed class OpenAiChoice
{
    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("message")]
    public required OpenAiMessage Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public required string FinishReason { get; init; }
}

public sealed class OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }
}

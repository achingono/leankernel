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

    /// <summary>
    /// Represents the open ai controller.
    /// </summary>
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

/// <summary>
/// Represents the open ai chat request.
/// </summary>
public sealed class OpenAiChatRequest
{
    /// <summary>
    /// Gets or sets the model.
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>
    /// Gets or sets the messages.
    /// </summary>
    [JsonPropertyName("messages")]
    public List<OpenAiMessage>? Messages { get; init; }

    /// <summary>
    /// Gets or sets the temperature.
    /// </summary>
    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    /// <summary>
    /// Gets or sets the max tokens.
    /// </summary>
    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Gets or sets the stream.
    /// </summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    /// <summary>
    /// Gets or sets the user.
    /// </summary>
    [JsonPropertyName("user")]
    public string? User { get; init; }
}

/// <summary>
/// Represents the open ai message.
/// </summary>
public sealed class OpenAiMessage
{
    /// <summary>
    /// Gets or sets the role.
    /// </summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>
    /// Gets or sets the content.
    /// </summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>
    /// Gets or sets the attachments.
    /// </summary>
    [JsonPropertyName("attachments")]
    public List<InboundAttachmentInput>? Attachments { get; init; }
}

/// <summary>
/// Represents the open ai chat response.
/// </summary>
public sealed class OpenAiChatResponse
{
    /// <summary>
    /// Gets or sets the id.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Gets or sets the object.
    /// </summary>
    [JsonPropertyName("object")]
    public required string Object { get; init; }

    /// <summary>
    /// Gets or sets the created.
    /// </summary>
    [JsonPropertyName("created")]
    public long Created { get; init; }

    /// <summary>
    /// Gets or sets the model.
    /// </summary>
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    /// <summary>
    /// Gets or sets the choices.
    /// </summary>
    [JsonPropertyName("choices")]
    public required List<OpenAiChoice> Choices { get; init; }

    /// <summary>
    /// Gets or sets the usage.
    /// </summary>
    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; init; }
}

/// <summary>
/// Represents the open ai choice.
/// </summary>
public sealed class OpenAiChoice
{
    /// <summary>
    /// Gets or sets the index.
    /// </summary>
    [JsonPropertyName("index")]
    public int Index { get; init; }

    /// <summary>
    /// Gets or sets the message.
    /// </summary>
    [JsonPropertyName("message")]
    public required OpenAiMessage Message { get; init; }

    /// <summary>
    /// Gets or sets the finish reason.
    /// </summary>
    [JsonPropertyName("finish_reason")]
    public required string FinishReason { get; init; }
}

/// <summary>
/// Represents the open ai usage.
/// </summary>
public sealed class OpenAiUsage
{
    /// <summary>
    /// Gets or sets the prompt tokens.
    /// </summary>
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    /// <summary>
    /// Gets or sets the completion tokens.
    /// </summary>
    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    /// <summary>
    /// Gets or sets the total tokens.
    /// </summary>
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }
}

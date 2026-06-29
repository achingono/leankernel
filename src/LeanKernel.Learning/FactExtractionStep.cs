using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Learning;

/// <summary>
/// Learning step that extracts factual information from conversation turns using an LLM.
/// Extracted facts are persisted to the knowledge store for future retrieval and context assembly.
/// </summary>
public sealed class FactExtractionStep(
    IHttpClientFactory httpClientFactory,
    IKnowledgeService knowledgeService,
    IOptions<LeanKernelConfig> config,
    ILogger<FactExtractionStep> logger) : ILearningStep
{
    internal const string HttpClientName = "learning-litellm";
    private const int MaxTranscriptChars = 12000;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly IKnowledgeService _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
    private readonly LeanKernelConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
    private readonly ILogger<FactExtractionStep> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public string Name => "fact-extraction";

    /// <inheritdoc/>
    public int Order => 10;

    /// <summary>
    /// Extracts factual information from a turn event by sending the conversation transcript to an LLM.
    /// Each extracted fact is stored as a knowledge page keyed by session, turn, and index.
    /// </summary>
    /// <param name="turnEvent">The turn event containing the conversation to extract facts from.</param>
    /// <param name="ct">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="LearningStepResult"/> indicating success and the number of facts extracted.</returns>
    public async Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(turnEvent);

        var userMessage = turnEvent.UserMessage;
        var assistantResponse = turnEvent.AssistantResponse;
        var combinedLength = (userMessage?.Length ?? 0) + (assistantResponse?.Length ?? turnEvent.Content.Length);
        if (combinedLength < _config.Learning.MinTurnLengthForExtraction)
        {
            return new LearningStepResult
            {
                StepName = Name,
                Success = true,
                ItemsLearned = 0,
            };
        }

        var request = new LiteLlmChatCompletionRequest
        {
            Model = string.IsNullOrWhiteSpace(_config.Learning.ExtractionModel)
                ? _config.LiteLlm.DefaultModel
                : _config.Learning.ExtractionModel,
            Temperature = _config.Learning.ExtractionTemperature,
            Messages =
            [
                new LiteLlmChatMessage
                {
                    Role = "system",
                    Content = "Extract any new factual information from this conversation that should be remembered. Return only a JSON array of strings. Return [] when there is nothing new."
                },
                new LiteLlmChatMessage
                {
                    Role = "user",
                    Content = BuildConversationTranscript(turnEvent)
                }
            ]
        };

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsJsonAsync("chat/completions", request, SerializerOptions, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning(
                "Fact extraction request failed with status {StatusCode} for session {SessionId} turn {TurnId}. Body: {Body}",
                (int)response.StatusCode,
                turnEvent.SessionId,
                turnEvent.TurnId,
                Truncate(errorBody, 1200));

            return new LearningStepResult
            {
                StepName = Name,
                Success = false,
                ItemsLearned = 0,
                Error = $"litellm_status_{(int)response.StatusCode}",
            };
        }

        var completion = await response.Content.ReadFromJsonAsync<LiteLlmChatCompletionResponse>(SerializerOptions, ct).ConfigureAwait(false);
        var content = completion?.Choices?
            .FirstOrDefault()?
            .Message?
            .Content;
        var facts = ParseFacts(content);

        for (var index = 0; index < facts.Count; index++)
        {
            var fact = facts[index];
            var key = LearningKeys.CreateFactPageKey(turnEvent.SessionId, turnEvent.TurnId, index + 1);
            await _knowledgeService.PutPageAsync(key, CreateFactPageContent(fact, turnEvent), ct).ConfigureAwait(false);
        }

        _logger.LogDebug(
            "Extracted {FactCount} facts for session {SessionId} turn {TurnId}",
            facts.Count,
            turnEvent.SessionId,
            turnEvent.TurnId);

        return new LearningStepResult
        {
            StepName = Name,
            Success = true,
            ItemsLearned = facts.Count,
            LearnedFacts = facts,
        };
    }

    /// <summary>
    /// Builds a conversation transcript from the turn event, including user message, recent history, and assistant response.
    /// </summary>
    private static string BuildConversationTranscript(TurnEvent turnEvent)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(turnEvent.UserMessage))
        {
            parts.Add($"User message:\n{turnEvent.UserMessage}");
        }

        if (turnEvent.Context?.History is { Count: > 0 } history)
        {
            var excerpt = history
                .TakeLast(4)
                .Select(turn => $"{turn.Role}: {turn.Content}");
            parts.Add("Recent history:\n" + string.Join("\n", excerpt));
        }

        parts.Add($"Assistant response:\n{turnEvent.AssistantResponse ?? turnEvent.Content}");
        var transcript = string.Join("\n\n", parts);
        return transcript.Length <= MaxTranscriptChars
            ? transcript
            : transcript[..MaxTranscriptChars];
    }

    /// <summary>
    /// Creates markdown content for a fact page including the fact text and metadata.
    /// </summary>
    private static string CreateFactPageContent(string fact, TurnEvent turnEvent)
        => $"# Learned Fact\n\n{fact}\n\n- Session: {turnEvent.SessionId}\n- Turn: {turnEvent.TurnId}\n- RecordedAt: {turnEvent.Timestamp:O}";

    /// <summary>
    /// Parses the LLM response content into a list of distinct fact strings.
    /// Handles JSON array responses, plain text, and bullet-pointed lists.
    /// </summary>
    private static IReadOnlyList<string> ParseFacts(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var trimmed = content.Trim();
        if (string.Equals(trimmed, "none", StringComparison.OrdinalIgnoreCase) || trimmed == "[]")
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return document.RootElement
                    .EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
        catch (JsonException)
        {
        }

        return trimmed
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.TrimStart('-', '*', '•', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ')', ' '))
            .Where(line => !string.IsNullOrWhiteSpace(line) && !string.Equals(line, "none", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Truncates a string to the specified maximum character length.
    /// </summary>
    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars];
    }
}

/// <summary>
/// Request model for the LiteLLM chat completion API used for fact extraction.
/// </summary>
internal sealed class LiteLlmChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("messages")]
    public List<LiteLlmChatMessage> Messages { get; set; } = [];
}

/// <summary>
/// Message model for LiteLLM chat completion requests and responses.
/// </summary>
internal sealed class LiteLlmChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Response model for the LiteLLM chat completion API.
/// </summary>
internal sealed class LiteLlmChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<LiteLlmChatChoice>? Choices { get; set; }
}

/// <summary>
/// Represents a single choice from the LiteLLM chat completion response.
/// </summary>
internal sealed class LiteLlmChatChoice
{
    [JsonPropertyName("message")]
    public LiteLlmChatMessage? Message { get; set; }
}

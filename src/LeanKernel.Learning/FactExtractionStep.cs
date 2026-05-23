using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Learning;

public sealed class FactExtractionStep(
    IHttpClientFactory httpClientFactory,
    IKnowledgeService knowledgeService,
    IOptions<LeanKernelConfig> config,
    ILogger<FactExtractionStep> logger) : ILearningStep
{
    internal const string HttpClientName = "learning-litellm";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly IKnowledgeService _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
    private readonly LeanKernelConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
    private readonly ILogger<FactExtractionStep> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public string Name => "fact-extraction";

    public int Order => 10;

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
        response.EnsureSuccessStatusCode();

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
        return string.Join("\n\n", parts);
    }

    private static string CreateFactPageContent(string fact, TurnEvent turnEvent)
        => $"# Learned Fact\n\n{fact}\n\n- Session: {turnEvent.SessionId}\n- Turn: {turnEvent.TurnId}\n- RecordedAt: {turnEvent.Timestamp:O}";

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
}

internal sealed class LiteLlmChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("messages")]
    public List<LiteLlmChatMessage> Messages { get; set; } = [];
}

internal sealed class LiteLlmChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

internal sealed class LiteLlmChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<LiteLlmChatChoice>? Choices { get; set; }
}

internal sealed class LiteLlmChatChoice
{
    [JsonPropertyName("message")]
    public LiteLlmChatMessage? Message { get; set; }
}

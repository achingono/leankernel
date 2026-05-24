using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Context.History;

public sealed class ConversationCompactor : IConversationCompactor
{
    private const string CompactPrompt = "Extract the key facts, decisions, and context from these conversation turns. Be concise.";
    private const string SummarizePrompt = "Summarize this conversation segment in one paragraph. Focus on outcomes and decisions.";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly LiteLlmConfig _liteLlmConfig;
    private readonly HistoryConfig _historyConfig;
    private readonly ILogger<ConversationCompactor> _logger;

    public ConversationCompactor(
        HttpClient httpClient,
        IOptions<LiteLlmConfig> liteLlmConfig,
        IOptions<HistoryConfig> historyConfig,
        ILogger<ConversationCompactor> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _liteLlmConfig = liteLlmConfig?.Value ?? throw new ArgumentNullException(nameof(liteLlmConfig));
        _historyConfig = historyConfig?.Value ?? throw new ArgumentNullException(nameof(historyConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(_liteLlmConfig.BaseUrl, UriKind.Absolute);
        }

        if (!string.IsNullOrWhiteSpace(_liteLlmConfig.ApiKey) && _httpClient.DefaultRequestHeaders.Authorization is null)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _liteLlmConfig.ApiKey);
        }
    }

    public Task<string> CompactAsync(IReadOnlyList<ConversationTurn> turns, CancellationToken ct = default)
        => SendCompletionAsync(CompactPrompt, turns, ct);

    public Task<string> SummarizeAsync(IReadOnlyList<ConversationTurn> turns, CancellationToken ct = default)
        => SendCompletionAsync(SummarizePrompt, turns, ct);

    private async Task<string> SendCompletionAsync(string instruction, IReadOnlyList<ConversationTurn> turns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(turns);

        if (turns.Count == 0)
        {
            return string.Empty;
        }

        var request = new ChatCompletionRequest(
            _historyConfig.CompactionModel,
            [
                new ChatMessage("system", instruction),
                new ChatMessage("user", RenderTurns(turns))
            ],
            _historyConfig.CompactionTemperature,
            _historyConfig.MaxSummaryTokens);

        _logger.LogDebug(
            "Sending {TurnCount} turns to LiteLLM compaction model {Model}",
            turns.Count,
            _historyConfig.CompactionModel);

        using var response = await _httpClient.PostAsJsonAsync("chat/completions", request, SerializerOptions, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(SerializerOptions, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("LiteLLM returned an empty compaction response.");

        var content = payload.Choices.FirstOrDefault()?.Message?.Content?.Trim();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("LiteLLM returned no compaction content.");
        }

        return content;
    }

    private static string RenderTurns(IReadOnlyList<ConversationTurn> turns)
        => string.Join(
            "\n\n",
            turns.Select((turn, index) => $"[{index + 1}] {turn.Role} ({turn.Timestamp:O})\n{turn.Content}"));

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaxTokens);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatCompletionResponse([property: JsonPropertyName("choices")] IReadOnlyList<ChatCompletionResponse.Choice> Choices)
    {
        public sealed record Choice([property: JsonPropertyName("message")] Message Message);

        public sealed record Message([property: JsonPropertyName("content")] string Content);
    }
}

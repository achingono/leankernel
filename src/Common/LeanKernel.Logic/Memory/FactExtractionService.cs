using System.Text.Json;

using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Memory;

/// <summary>
/// Extracts durable facts from conversation turns and renders seed memory pages.
/// </summary>
public sealed class FactExtractionService
{
    private const int MaxTranscriptChars = 12000;
    private readonly IChatClient _chatClient;
    private readonly FactExtractionSettings _settings;
    private readonly MemoryPageRenderer _renderer;

    /// <summary>
    /// Initializes a new instance of the <see cref="FactExtractionService"/> class.
    /// </summary>
    /// <param name="chatClient">The chat client used for fact extraction prompts.</param>
    /// <param name="settings">The fact extraction settings.</param>
    /// <param name="renderer">The renderer used to produce seed pages.</param>
    public FactExtractionService(
        [FromKeyedServices("fact-extraction")] IChatClient chatClient,
        IOptions<FactExtractionSettings> settings,
        MemoryPageRenderer renderer)
    {
        _chatClient = chatClient;
        _settings = settings.Value;
        _renderer = renderer;
    }

    /// <summary>
    /// Extracts distinct factual statements from the latest conversation exchange.
    /// </summary>
    /// <param name="userMessage">The latest user message, if any.</param>
    /// <param name="assistantResponse">The assistant response text.</param>
    /// <param name="recentHistory">Recent conversation history to use as context.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The extracted fact strings.</returns>
    public async Task<IReadOnlyList<string>> ExtractFactsAsync(
        string? userMessage,
        string assistantResponse,
        IReadOnlyList<ChatMessage> recentHistory,
        CancellationToken cancellationToken = default)
    {
        var transcript = BuildConversationTranscript(userMessage, assistantResponse, recentHistory);
        var response = await _chatClient.GetResponseAsync(
            [
                new ChatMessage(
                    ChatRole.System,
                    "Extract any new factual information from this conversation that should be remembered. Return only a JSON array of strings. Return [] when there is nothing new."),
                new ChatMessage(ChatRole.User, transcript)
            ],
            new ChatOptions
            {
                Temperature = (float)_settings.Temperature,
                MaxOutputTokens = _settings.MaxOutputTokens
            },
            cancellationToken).ConfigureAwait(false);

        var text = response.Messages?.FirstOrDefault()?.Text;
        return ParseFacts(text);
    }

    /// <summary>
    /// Renders a seed memory page for a fact before normalization.
    /// </summary>
    /// <param name="fact">The fact text to render.</param>
    /// <param name="sessionId">The optional session identifier.</param>
    /// <param name="turnId">The optional turn identifier.</param>
    /// <param name="recordedAt">The timestamp associated with the fact.</param>
    /// <returns>The rendered seed page content.</returns>
    public string RenderSeedPage(string fact, string? sessionId, string? turnId, DateTimeOffset recordedAt)
    {
        return _renderer.RenderSeedPage(fact, sessionId, turnId, recordedAt);
    }

    /// <summary>
    /// Builds a compact transcript from the latest exchange and recent history.
    /// </summary>
    /// <param name="userMessage">The latest user message, if any.</param>
    /// <param name="assistantResponse">The assistant response text.</param>
    /// <param name="recentHistory">Recent conversation history to include.</param>
    /// <returns>The transcript text used for fact extraction.</returns>
    public static string BuildConversationTranscript(
        string? userMessage,
        string assistantResponse,
        IReadOnlyList<ChatMessage> recentHistory)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(userMessage))
        {
            parts.Add($"User message:\n{userMessage}");
        }

        if (recentHistory.Count > 0)
        {
            var excerpt = recentHistory.TakeLast(4).Select(static turn => $"{turn.Role}: {turn.Text}");
            parts.Add("Recent history:\n" + string.Join("\n", excerpt));
        }

        parts.Add($"Assistant response:\n{assistantResponse}");
        var transcript = string.Join("\n\n", parts);
        return transcript.Length <= MaxTranscriptChars
            ? transcript
            : transcript[..MaxTranscriptChars];
    }

    /// <summary>
    /// Parses extracted fact output from JSON or line-based text into distinct facts.
    /// </summary>
    /// <param name="content">The raw model output to parse.</param>
    /// <returns>The parsed fact strings.</returns>
    public static IReadOnlyList<string> ParseFacts(string? content)
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
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(static item => item.GetString())
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Select(static item => item!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
        catch (JsonException)
        {
            // Fall through to line-based parsing below.
        }

        return trimmed
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => line.TrimStart('-', '*', '•', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ')', ' '))
            .Where(static line => !string.IsNullOrWhiteSpace(line) && !string.Equals(line, "none", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
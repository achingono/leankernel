using System.Text.Json;
using LeanKernel.Logic.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Memory;

public sealed class FactExtractionService
{
    private const int MaxTranscriptChars = 12000;
    private readonly IChatClient _chatClient;
    private readonly FactExtractionSettings _settings;
    private readonly MemoryPageRenderer _renderer;

    public FactExtractionService(
        [FromKeyedServices("fact-extraction")] IChatClient chatClient,
        IOptions<FactExtractionSettings> settings,
        MemoryPageRenderer renderer)
    {
        _chatClient = chatClient;
        _settings = settings.Value;
        _renderer = renderer;
    }

    public async Task<IReadOnlyList<string>> ExtractFactsAsync(
        string? userMessage,
        string assistantResponse,
        IReadOnlyList<ChatMessage> recentHistory,
        CancellationToken cancellationToken = default)
    {
        var transcript = BuildConversationTranscript(userMessage, assistantResponse, recentHistory);
        var response = await _chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System,
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

    public string RenderSeedPage(string fact, string? sessionId, string? turnId, DateTimeOffset recordedAt)
    {
        return _renderer.RenderSeedPage(fact, sessionId, turnId, recordedAt);
    }

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
        }

        return trimmed
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static line => line.TrimStart('-', '*', '•', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', ')', ' '))
            .Where(static line => !string.IsNullOrWhiteSpace(line) && !string.Equals(line, "none", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

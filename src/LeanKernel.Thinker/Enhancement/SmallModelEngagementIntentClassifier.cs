using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Thinker.Enhancement;

/// <summary>
/// Uses the configured small chat model as an advisory semantic classifier for engagement updates.
/// </summary>
public sealed class SmallModelEngagementIntentClassifier : IEngagementIntentClassifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex FencedJsonRegex = new(
        "```(?:json)?\\s*(\\{[\\s\\S]*?\\})\\s*```",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ObjectRegex = new(
        "\\{[\\s\\S]*\\}",
        RegexOptions.Compiled);
    private static readonly string CategoriesForPrompt = string.Join(
        " | ",
        EngagementIntentCategories.All.Select(static c => $"\"{c}\""));
    private static readonly string SystemPromptTemplate = """
        You classify whether a user's message should update a personal AI agent's engagement identity files.

        Return only compact JSON with:
        {
          "shouldUpdate": boolean,
          "category": __CATEGORIES__,
          "normalizedInsight": "short durable preference/fact to store, or empty",
          "reason": "short reason"
        }

        Mark shouldUpdate true when the user expresses a durable preference, correction, operating rule, agent identity preference, communication style change, autonomy preference, schedule/time boundary, tooling preference, or long-term priority.
        Examples:
        - "I wish you would communicate more directly" => true, communication, "communicate more directly"
        - "Don't resurface completed tasks" => true, tools, "check task completion status before resurfacing tasks"
        - "Saturdays are Sabbath; avoid work conversations" => true, time_boundary, "Saturdays are Sabbath; avoid work conversations"
        - "How should I proceed with testing?" => false, none, ""

        Do not classify ordinary task requests, one-off questions, or implementation discussion as engagement updates unless they express a durable preference or correction.
        """;

    private readonly AgentFactory _agentFactory;
    private readonly ILogger<SmallModelEngagementIntentClassifier> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SmallModelEngagementIntentClassifier" /> class.
    /// </summary>
    public SmallModelEngagementIntentClassifier(
        AgentFactory agentFactory,
        ILogger<SmallModelEngagementIntentClassifier> logger)
    {
        _agentFactory = agentFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EngagementIntentClassification> ClassifyAsync(string userMessage, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return EngagementIntentClassification.NoUpdate("Empty user message.");

        try
        {
            var messages = new[]
            {
                new ChatMessage(
                    ChatRole.System,
                    SystemPromptTemplate.Replace("__CATEGORIES__", CategoriesForPrompt, StringComparison.Ordinal)),
                new ChatMessage(ChatRole.User, userMessage)
            };

            var response = await _agentFactory.ChatClient.GetResponseAsync(
                messages,
                new ChatOptions { Temperature = 0 },
                ct);

            return Parse(response.Text ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Small-model engagement intent classification failed.");
            return EngagementIntentClassification.NoUpdate("Classifier unavailable.");
        }
    }

    private static EngagementIntentClassification Parse(string text)
    {
        var json = ExtractJson(text);
        if (string.IsNullOrWhiteSpace(json))
            return EngagementIntentClassification.NoUpdate("Classifier returned no JSON.");

        try
        {
            var result = JsonSerializer.Deserialize<ClassifierJson>(json, JsonOptions);
            if (result is null)
                return EngagementIntentClassification.NoUpdate("Classifier JSON was empty.");

            return new EngagementIntentClassification(
                result.ShouldUpdate,
                EngagementIntentCategories.NormalizeOrNone(result.Category),
                result.NormalizedInsight?.Trim() ?? "",
                result.Reason?.Trim() ?? "");
        }
        catch (JsonException)
        {
            return EngagementIntentClassification.NoUpdate("Classifier JSON was invalid.");
        }
    }

    private static string? ExtractJson(string text)
    {
        var fenced = FencedJsonRegex.Match(text);
        if (fenced.Success)
            return fenced.Groups[1].Value;

        var objectMatch = ObjectRegex.Match(text);
        return objectMatch.Success ? objectMatch.Value : null;
    }

    private sealed class ClassifierJson
    {
        public bool ShouldUpdate { get; set; }
        public string? Category { get; set; }
        public string? NormalizedInsight { get; set; }
        public string? Reason { get; set; }
    }
}

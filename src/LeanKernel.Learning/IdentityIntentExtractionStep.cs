using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Learning;

public sealed class IdentityIntentExtractionStep(
    IHttpClientFactory httpClientFactory,
    IKnowledgeService knowledgeService,
    KnowledgePageUpdateCoordinator updateCoordinator,
    IOptions<LeanKernelConfig> config,
    ILogger<IdentityIntentExtractionStep> logger) : ILearningStep
{
    internal const string HttpClientName = "learning-intent-litellm";
    private const int MaxIntentTranscriptChars = 12000;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> BehaviorFieldAllowlist =
    [
        "autonomy_level",
        "communication_style",
        "work_style",
        "tool_preferences"
    ];

    private static readonly string[] NoIntentMarkers =
    [
        "none",
        "n/a",
        "no behavior intent"
    ];

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    private readonly IKnowledgeService _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
    private readonly KnowledgePageUpdateCoordinator _updateCoordinator = updateCoordinator ?? throw new ArgumentNullException(nameof(updateCoordinator));
    private readonly LeanKernelConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
    private readonly ILogger<IdentityIntentExtractionStep> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public string Name => "identity-intent-extraction";

    public int Order => 15;

    public Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(turnEvent);

        if (!_config.Learning.IntentExtractionEnabled)
        {
            return Task.FromResult(new LearningStepResult
            {
                StepName = Name,
                Success = true,
                ItemsLearned = 0,
            });
        }

        if (string.IsNullOrWhiteSpace(turnEvent.UserMessage))
        {
            return Task.FromResult(new LearningStepResult
            {
                StepName = Name,
                Success = true,
                ItemsLearned = 0,
            });
        }

        return ProcessInternalAsync(turnEvent, ct);
    }

    private async Task<LearningStepResult> ProcessInternalAsync(TurnEvent turnEvent, CancellationToken ct)
    {
        var extractionResult = await ExtractIntentAsync(turnEvent, ct).ConfigureAwait(false);

        var allowedIdentityFields = new HashSet<string>(
            _config.Identity.AllowedIdentityFields.Where(field => !string.IsNullOrWhiteSpace(field)),
            StringComparer.OrdinalIgnoreCase);

        var minConfidence = Math.Clamp(_config.Learning.IntentExtractionMinConfidence, 0.0, 1.0);
        var maxUpdates = Math.Max(1, _config.Learning.IntentExtractionMaxUpdatesPerTurn);

        var candidateUpdates = extractionResult.Updates
            .Where(update => !string.IsNullOrWhiteSpace(update.Field))
            .Where(update => BehaviorFieldAllowlist.Contains(update.Field!))
            .Where(update => allowedIdentityFields.Contains(update.Field!))
            .Where(update => !string.IsNullOrWhiteSpace(update.Value))
            .Where(update => !NoIntentMarkers.Contains(update.Value!.Trim(), StringComparer.OrdinalIgnoreCase))
            .Select(update => update with
            {
                Field = update.Field!.Trim(),
                Value = NormalizeValue(update.Field!, update.Value!),
                Confidence = Math.Clamp(update.Confidence, 0.0, 1.0),
            })
            .Where(update => !string.IsNullOrWhiteSpace(update.Value))
            .Where(update => update.Confidence >= minConfidence)
            .GroupBy(update => update.Field!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Confidence).First())
            .Take(maxUpdates)
            .ToArray();

        if (candidateUpdates.Length == 0)
        {
            return new LearningStepResult
            {
                StepName = Name,
                Success = true,
                ItemsLearned = 0,
            };
        }

        var pageKey = _config.Identity.UserPreferencePageKey;
        var appliedFields = await _updateCoordinator.ExecuteAsync(
            pageKey,
            cancellationToken => ApplyUpdatesAsync(pageKey, turnEvent, candidateUpdates, cancellationToken),
            ct).ConfigureAwait(false);

        return new LearningStepResult
        {
            StepName = Name,
            Success = true,
            ItemsLearned = appliedFields.Count,
            LearnedFacts = appliedFields,
        };
    }

    private async Task<IReadOnlyList<string>> ApplyUpdatesAsync(
        string pageKey,
        TurnEvent turnEvent,
        IReadOnlyList<IdentityIntentUpdate> updates,
        CancellationToken ct)
    {
        var existing = await _knowledgeService.GetPageAsync(pageKey, ct).ConfigureAwait(false);
        var document = ParsedIdentityDocument.Parse(existing?.Content);

        document.Metadata.TryAdd("id", pageKey);
        document.Metadata.TryAdd("pageType", "identity-user-preferences");
        document.Metadata.TryAdd("subject", turnEvent.Context?.Identity?.UserId ?? "primary-user");
        document.Metadata.TryAdd("scope", "private");
        document.Metadata.TryAdd("sourceOfTruth", "gbrain");

        var changed = false;
        var applied = new List<string>();
        var now = DateTimeOffset.UtcNow;

        foreach (var update in updates)
        {
            if (document.Fields.TryGetValue(update.Field!, out var existingField))
            {
                if (existingField.Confidence > update.Confidence)
                {
                    _logger.LogDebug(
                        "Skipping identity intent update for field {Field}: existing confidence {ExistingConfidence} > extracted confidence {ExtractedConfidence}",
                        update.Field,
                        existingField.Confidence,
                        update.Confidence);
                    continue;
                }

                if (string.Equals(existingField.Value.Trim(), update.Value!.Trim(), StringComparison.OrdinalIgnoreCase)
                    && existingField.Confidence >= update.Confidence)
                {
                    continue;
                }
            }

            document.Fields[update.Field!] = new ParsedIdentityField
            {
                Name = update.Field!,
                Value = update.Value!,
                Confidence = update.Confidence,
                LastUpdated = now,
                Source = "user_intent_extraction",
            };
            applied.Add(update.Field!);
            changed = true;
        }

        if (!changed)
        {
            return [];
        }

        var serialized = document.Serialize();
        if (!string.Equals(existing?.Content, serialized, StringComparison.Ordinal))
        {
            await _knowledgeService.PutPageAsync(pageKey, serialized, ct).ConfigureAwait(false);
        }

        return applied;
    }

    private async Task<IdentityIntentExtractionResult> ExtractIntentAsync(TurnEvent turnEvent, CancellationToken ct)
    {
        var request = new LiteLlmChatCompletionRequest
        {
            Model = string.IsNullOrWhiteSpace(_config.Learning.IntentExtractionModel)
                ? _config.LiteLlm.DefaultModel
                : _config.Learning.IntentExtractionModel,
            Temperature = _config.Learning.IntentExtractionTemperature,
            Messages =
            [
                new LiteLlmChatMessage
                {
                    Role = "system",
                    Content =
                        "You extract user intent about assistant behavior preferences. " +
                        "Return strict JSON object with shape: {\"hasBehaviorIntent\": boolean, \"updates\": [{\"field\": string, \"value\": string, \"confidence\": number, \"reason\": string}]}. " +
                        "Only use fields: autonomy_level, communication_style, work_style, tool_preferences. " +
                        "If no intent is present return {\"hasBehaviorIntent\":false,\"updates\":[]}."
                },
                new LiteLlmChatMessage
                {
                    Role = "user",
                    Content = BuildIntentTranscript(turnEvent)
                }
            ]
        };

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.PostAsJsonAsync("chat/completions", request, SerializerOptions, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning(
                "Identity intent extraction request failed with status {StatusCode} for session {SessionId} turn {TurnId}. Body: {Body}",
                (int)response.StatusCode,
                turnEvent.SessionId,
                turnEvent.TurnId,
                Truncate(errorBody, 1200));
            return IdentityIntentExtractionResult.Empty;
        }

        var completion = await response.Content.ReadFromJsonAsync<LiteLlmChatCompletionResponse>(SerializerOptions, ct).ConfigureAwait(false);
        var content = completion?.Choices?.FirstOrDefault()?.Message?.Content;
        return ParseExtraction(content);
    }

    private static string BuildIntentTranscript(TurnEvent turnEvent)
    {
        var builder = new StringBuilder();
        builder.AppendLine("User message:");
        builder.AppendLine(turnEvent.UserMessage);
        builder.AppendLine();
        builder.AppendLine("Assistant response (context only):");
        builder.AppendLine(turnEvent.AssistantResponse ?? turnEvent.Content);

        if (turnEvent.Context?.History is { Count: > 0 } history)
        {
            builder.AppendLine();
            builder.AppendLine("Recent history:");
            foreach (var turn in history.TakeLast(4))
            {
                builder.AppendLine($"{turn.Role}: {turn.Content}");
            }
        }

        var transcript = builder.ToString();
        return transcript.Length <= MaxIntentTranscriptChars
            ? transcript
            : transcript[..MaxIntentTranscriptChars];
    }

    private static IdentityIntentExtractionResult ParseExtraction(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return IdentityIntentExtractionResult.Empty;
        }

        var trimmed = content.Trim();
        try
        {
            var parsed = JsonSerializer.Deserialize<IdentityIntentExtractionResult>(trimmed, SerializerOptions);
            return parsed ?? IdentityIntentExtractionResult.Empty;
        }
        catch (JsonException)
        {
            return IdentityIntentExtractionResult.Empty;
        }
    }

    private static string NormalizeValue(string field, string rawValue)
    {
        var value = rawValue.Trim();
        if (string.Equals(field, "autonomy_level", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = value.ToLowerInvariant();
            if (normalized.Contains("proactive", StringComparison.Ordinal)
                || normalized.Contains("autonomous", StringComparison.Ordinal)
                || normalized.Contains("don't ask", StringComparison.Ordinal)
                || normalized.Contains("do not ask", StringComparison.Ordinal))
            {
                return "proactive-by-default-unless-destructive";
            }

            if (normalized.Contains("high", StringComparison.Ordinal) || normalized.Contains("mostly autonomous", StringComparison.Ordinal))
            {
                return "high";
            }

            if (normalized.Contains("low", StringComparison.Ordinal) || normalized.Contains("ask first", StringComparison.Ordinal))
            {
                return "low";
            }

            return "medium";
        }

        return value.Length > 400 ? value[..400].Trim() : value;
    }

    private static string Truncate(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars];
    }

    private sealed record IdentityIntentExtractionResult
    {
        [JsonPropertyName("hasBehaviorIntent")]
        public bool HasBehaviorIntent { get; init; }

        [JsonPropertyName("updates")]
        public IReadOnlyList<IdentityIntentUpdate> Updates { get; init; } = [];

        public static IdentityIntentExtractionResult Empty { get; } = new();
    }

    private sealed record IdentityIntentUpdate
    {
        [JsonPropertyName("field")]
        public string? Field { get; init; }

        [JsonPropertyName("value")]
        public string? Value { get; init; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; init; }

        [JsonPropertyName("reason")]
        public string? Reason { get; init; }
    }

    private sealed class ParsedIdentityDocument
    {
        private static readonly HashSet<string> ReservedMetadataKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "id",
            "pageType",
            "subject",
            "scope",
            "sourceOfTruth"
        };

        public Dictionary<string, string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ParsedIdentityField> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string Body { get; private set; } = string.Empty;

        public static ParsedIdentityDocument Parse(string? content)
        {
            var document = new ParsedIdentityDocument();
            var normalized = NormalizeNewLines(content);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return document;
            }

            if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            {
                document.Body = normalized.Trim();
                return document;
            }

            var closingIndex = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
            if (closingIndex < 0)
            {
                document.Body = normalized.Trim();
                return document;
            }

            var frontmatter = normalized[4..closingIndex];
            document.Body = normalized[(closingIndex + 5)..].Trim();
            var lines = frontmatter.Split('\n');
            string? currentKey = null;
            var currentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawLine in lines)
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                var indent = rawLine.Length - rawLine.TrimStart().Length;
                var trimmed = rawLine.Trim();

                if (indent == 0)
                {
                    FlushCurrentField(document, currentKey, currentMap);
                    currentMap.Clear();
                    currentKey = null;

                    var separatorIndex = trimmed.IndexOf(':');
                    if (separatorIndex < 0)
                    {
                        continue;
                    }

                    var key = trimmed[..separatorIndex].Trim();
                    var value = trimmed[(separatorIndex + 1)..].Trim();

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        currentKey = key;
                        continue;
                    }

                    var scalar = Unquote(value);
                    if (ReservedMetadataKeys.Contains(key))
                    {
                        document.Metadata[key] = scalar;
                    }
                    else
                    {
                        document.Fields[key] = new ParsedIdentityField
                        {
                            Name = key,
                            Value = scalar,
                        };
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(currentKey))
                {
                    continue;
                }

                var nestedSeparator = trimmed.IndexOf(':');
                if (nestedSeparator < 0)
                {
                    continue;
                }

                var nestedKey = trimmed[..nestedSeparator].Trim();
                var nestedValue = Unquote(trimmed[(nestedSeparator + 1)..].Trim());
                currentMap[nestedKey] = nestedValue;
            }

            FlushCurrentField(document, currentKey, currentMap);
            return document;
        }

        public string Serialize()
        {
            var builder = new StringBuilder();
            builder.AppendLine("---");

            foreach (var entry in Metadata.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                builder.Append(entry.Key);
                builder.Append(": ");
                builder.AppendLine(Quote(entry.Value));
            }

            foreach (var field in Fields.Values.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                builder.Append(field.Name);
                builder.AppendLine(":");
                builder.AppendLine($"  value: {Quote(field.Value)}");
                builder.AppendLine($"  confidence: {field.Confidence.ToString("0.###", CultureInfo.InvariantCulture)}");
                builder.AppendLine($"  last_updated: {field.LastUpdated.ToString("O", CultureInfo.InvariantCulture)}");
                if (!string.IsNullOrWhiteSpace(field.Source))
                {
                    builder.AppendLine($"  source: {Quote(field.Source)}");
                }
            }

            builder.AppendLine("---");
            if (!string.IsNullOrWhiteSpace(Body))
            {
                builder.Append(Body.Trim());
            }

            return builder.ToString();
        }

        private static void FlushCurrentField(
            ParsedIdentityDocument document,
            string? key,
            IReadOnlyDictionary<string, string> map)
        {
            if (string.IsNullOrWhiteSpace(key) || map.Count == 0)
            {
                return;
            }

            if (ReservedMetadataKeys.Contains(key))
            {
                if (map.TryGetValue("value", out var metadataValue) && !string.IsNullOrWhiteSpace(metadataValue))
                {
                    document.Metadata[key] = metadataValue;
                }

                return;
            }

            if (!map.TryGetValue("value", out var value) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var confidence = 0.5;
            if (map.TryGetValue("confidence", out var confidenceText)
                && double.TryParse(confidenceText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedConfidence))
            {
                confidence = Math.Clamp(parsedConfidence, 0.0, 1.0);
            }

            var lastUpdated = DateTimeOffset.UtcNow;
            if (map.TryGetValue("last_updated", out var lastUpdatedText)
                && DateTimeOffset.TryParse(lastUpdatedText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedLastUpdated))
            {
                lastUpdated = parsedLastUpdated;
            }

            map.TryGetValue("source", out var source);
            document.Fields[key] = new ParsedIdentityField
            {
                Name = key,
                Value = value,
                Confidence = confidence,
                LastUpdated = lastUpdated,
                Source = source ?? "unknown",
            };
        }

        private static string NormalizeNewLines(string? value)
            => string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

        private static string Quote(string value)
            => $"'{(value ?? string.Empty).Replace("'", "''", StringComparison.Ordinal)}'";

        private static string Unquote(string value)
        {
            if (value.Length >= 2
                && ((value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal))
                    || (value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal))))
            {
                return value[1..^1].Replace("''", "'", StringComparison.Ordinal);
            }

            return value;
        }
    }

    private sealed class ParsedIdentityField
    {
        public required string Name { get; init; }

        public string Value { get; init; } = string.Empty;

        public double Confidence { get; init; } = 0.5;

        public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;

        public string Source { get; init; } = "unknown";
    }
}

using System.Globalization;
using System.Text;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Context.Identity;

/// <summary>
/// Loads durable identity pages from GBrain and projects them into prompt-safe context.
/// </summary>
public sealed class IdentityProvider : IIdentityProvider
{
    private readonly IKnowledgeService _knowledgeService;
    private readonly IdentityConfig _config;
    private readonly ILogger<IdentityProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityProvider"/> class.
    /// </summary>
    /// <param name="knowledgeService">The knowledge service used to read GBrain pages.</param>
    /// <param name="config">The identity configuration.</param>
    /// <param name="logger">The logger.</param>
    public IdentityProvider(
        IKnowledgeService knowledgeService,
        IOptions<IdentityConfig> config,
        ILogger<IdentityProvider> logger)
    {
        _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IdentityContext> LoadIdentityAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var agentKey = _config.EnableUserScopedKeys && !string.IsNullOrWhiteSpace(userId)
            ? $"{_config.AgentProfilePageKey}-{userId}"
            : _config.AgentProfilePageKey;
        var userKey = _config.EnableUserScopedKeys && !string.IsNullOrWhiteSpace(userId)
            ? $"{_config.UserPreferencePageKey}-{userId}"
            : _config.UserPreferencePageKey;

        var agentPageTask = _knowledgeService.GetPageAsync(agentKey, ct);
        var userPageTask = _knowledgeService.GetPageAsync(userKey, ct);

        await Task.WhenAll(agentPageTask, userPageTask).ConfigureAwait(false);

        var agentPage = agentPageTask.Result;
        var userPage = userPageTask.Result;

        var promptSegments = new List<string>();
        var confidenceValues = new List<double>();

        var agentIdentityPage = CreateIdentityPage(agentPage, "Agent Profile", promptSegments, confidenceValues);
        var userIdentityPage = CreateIdentityPage(userPage, "User Preferences", promptSegments, confidenceValues);

        var overallConfidence = confidenceValues.Count > 0
            ? confidenceValues.Average()
            : promptSegments.Count > 0 ? 0.5 : 0.0;

        _logger.LogDebug(
            "Loaded identity for user {UserId}: agent page {HasAgentPage}, user page {HasUserPage}, confidence {Confidence}",
            userId,
            agentIdentityPage is not null,
            userIdentityPage is not null,
            overallConfidence);

        return new IdentityContext
        {
            UserId = userId,
            AgentProfile = agentIdentityPage,
            UserPreferences = userIdentityPage,
            PromptSegments = promptSegments,
            OverallConfidence = overallConfidence,
        };
    }

    private IdentityPage? CreateIdentityPage(
        KnowledgePage? page,
        string title,
        ICollection<string> promptSegments,
        ICollection<double> confidenceValues)
    {
        if (page is null)
        {
            return null;
        }

        var document = IdentityPageSerializer.ParseDocument(page.Content, _logger);
        var identityPage = new IdentityPage
        {
            Key = page.Key,
            Content = page.Content,
            Fields = document.Fields,
        };

        foreach (var field in document.Fields.Values)
        {
            confidenceValues.Add(Math.Clamp(field.Confidence, 0.0, 1.0));
        }

        var segment = BuildPromptSegment(title, identityPage, document.Body);
        if (!string.IsNullOrWhiteSpace(segment))
        {
            promptSegments.Add(segment);
        }

        return identityPage;
    }

    private static string? BuildPromptSegment(string title, IdentityPage page, string body)
    {
        var lines = new List<string>
        {
            $"### {title} ({page.Key})"
        };

        foreach (var field in page.Fields.Values.OrderBy(static field => field.Name, StringComparer.Ordinal))
        {
            lines.Add($"- {field.Name}: {field.Value}");
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            lines.Add($"Summary: {body.Trim()}");
        }

        return lines.Count > 1 || !string.IsNullOrWhiteSpace(body)
            ? string.Join("\n", lines)
            : null;
    }
}

internal static class IdentityPageSerializer
{
    private static readonly HashSet<string> ReservedMetadataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "pageType",
        "subject",
        "scope",
        "sourceOfTruth"
    };

    public static ParsedIdentityDocument ParseDocument(string? content, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var normalized = NormalizeNewLines(content);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new ParsedIdentityDocument();
        }

        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return new ParsedIdentityDocument
            {
                Body = normalized.Trim(),
            };
        }

        var closingIndex = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (closingIndex < 0)
        {
            logger.LogWarning("Identity page frontmatter is malformed; falling back to raw content");
            return new ParsedIdentityDocument
            {
                Body = normalized.Trim(),
                HasMalformedFrontmatter = true,
            };
        }

        var frontmatter = normalized[4..closingIndex];
        var body = normalized[(closingIndex + 5)..].Trim();

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fields = new Dictionary<string, IdentityField>(StringComparer.OrdinalIgnoreCase);
        var lines = frontmatter.Split('\n');
        string? currentKey = null;
        Dictionary<string, string>? currentMap = null;
        List<string>? currentList = null;

        void FinalizeCurrent()
        {
            if (string.IsNullOrWhiteSpace(currentKey))
            {
                return;
            }

            if (ReservedMetadataKeys.Contains(currentKey))
            {
                StoreReservedMetadata(currentKey, currentMap, currentList, metadata);
            }
            else
            {
                StoreFieldIfNonEmpty(currentKey, currentMap, currentList, fields);
            }

            currentKey = null;
            currentMap = null;
            currentList = null;
        }

        foreach (var rawLine in lines)
        {
            if (TrySkipIgnoredLine(rawLine))
            {
                continue;
            }

            var trimmedLine = rawLine.Trim();
            var indent = rawLine.Length - rawLine.TrimStart().Length;

            if (indent == 0)
            {
                FinalizeCurrent();
                ProcessTopLevelField(trimmedLine, metadata, fields, ref currentKey, ref currentMap, ref currentList);
            }
            else if (!string.IsNullOrWhiteSpace(currentKey))
            {
                ProcessIndentedField(trimmedLine, ref currentMap, ref currentList);
            }
        }

        FinalizeCurrent();

        return new ParsedIdentityDocument
        {
            Metadata = metadata,
            Fields = fields,
            Body = body,
        };
    }

    private static bool TrySkipIgnoredLine(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine))
        {
            return true;
        }

        var trimmedLine = rawLine.Trim();
        return trimmedLine.StartsWith("#", StringComparison.Ordinal);
    }

    private static void StoreReservedMetadata(
        string currentKey,
        Dictionary<string, string>? currentMap,
        List<string>? currentList,
        Dictionary<string, string> metadata)
    {
        if (currentMap is not null && currentMap.TryGetValue("value", out var metadataValue) && !string.IsNullOrWhiteSpace(metadataValue))
        {
            metadata[currentKey] = metadataValue;
        }
        else if (currentList is not null && currentList.Count > 0)
        {
            metadata[currentKey] = string.Join(", ", currentList);
        }
    }

    private static void StoreFieldIfNonEmpty(
        string currentKey,
        Dictionary<string, string>? currentMap,
        List<string>? currentList,
        Dictionary<string, IdentityField> fields)
    {
        var value = ResolveFieldValue(currentMap, currentList);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        fields[currentKey] = new IdentityField
        {
            Name = currentKey,
            Value = value,
            Confidence = ResolveConfidence(currentMap),
            LastUpdated = ResolveLastUpdated(currentMap),
            Source = ResolveSource(currentMap),
        };
    }

    private static void ProcessTopLevelField(
        string trimmedLine,
        Dictionary<string, string> metadata,
        Dictionary<string, IdentityField> fields,
        ref string? currentKey,
        ref Dictionary<string, string>? currentMap,
        ref List<string>? currentList)
    {
        var separatorIndex = trimmedLine.IndexOf(':');
        if (separatorIndex < 0)
        {
            return;
        }

        var key = trimmedLine[..separatorIndex].Trim();
        var rawValue = trimmedLine[(separatorIndex + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            currentKey = key;
            currentMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            currentList = [];
            return;
        }

        var normalizedValue = NormalizeScalar(rawValue);
        if (ReservedMetadataKeys.Contains(key))
        {
            metadata[key] = normalizedValue;
        }
        else
        {
            fields[key] = new IdentityField
            {
                Name = key,
                Value = normalizedValue,
            };
        }
    }

    private static void ProcessIndentedField(
        string trimmedLine,
        ref Dictionary<string, string>? currentMap,
        ref List<string>? currentList)
    {
        if (trimmedLine.StartsWith("- ", StringComparison.Ordinal))
        {
            currentList ??= [];
            currentList.Add(NormalizeScalar(trimmedLine[2..]));
            return;
        }

        var nestedSeparatorIndex = trimmedLine.IndexOf(':');
        if (nestedSeparatorIndex < 0)
        {
            return;
        }

        var nestedKey = trimmedLine[..nestedSeparatorIndex].Trim();
        var nestedValue = NormalizeScalar(trimmedLine[(nestedSeparatorIndex + 1)..].Trim());
        currentMap ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        currentMap[nestedKey] = nestedValue;
    }

    public static string SerializeDocument(
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyDictionary<string, IdentityField> fields,
        string? body)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");

        foreach (var entry in metadata.OrderBy(static item => item.Key, StringComparer.Ordinal))
        {
            builder.Append(entry.Key);
            builder.Append(": ");
            builder.AppendLine(QuoteScalar(entry.Value));
        }

        foreach (var field in fields.Values.OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            builder.Append(field.Name);
            builder.AppendLine(":");
            builder.AppendLine($"  value: {QuoteScalar(field.Value)}");
            builder.AppendLine($"  confidence: {field.Confidence.ToString("0.###", CultureInfo.InvariantCulture)}");
            builder.AppendLine($"  last_updated: {field.LastUpdated.ToString("O", CultureInfo.InvariantCulture)}");
            if (!string.IsNullOrWhiteSpace(field.Source))
            {
                builder.AppendLine($"  source: {QuoteScalar(field.Source)}");
            }
        }

        builder.AppendLine("---");
        if (!string.IsNullOrWhiteSpace(body))
        {
            builder.Append(body.Trim());
        }

        return builder.ToString();
    }

    private static string ResolveFieldValue(IReadOnlyDictionary<string, string>? map, IReadOnlyList<string>? list)
    {
        if (map is not null && map.TryGetValue("value", out var explicitValue) && !string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue;
        }

        if (list is not null && list.Count > 0)
        {
            return string.Join(", ", list);
        }

        if (map is not null && map.Count == 1)
        {
            return map.Values.First();
        }

        return string.Empty;
    }

    private static double ResolveConfidence(IReadOnlyDictionary<string, string>? map)
    {
        if (map is not null
            && map.TryGetValue("confidence", out var rawConfidence)
            && double.TryParse(rawConfidence, CultureInfo.InvariantCulture, out var confidence))
        {
            return Math.Clamp(confidence, 0.0, 1.0);
        }

        return 1.0;
    }

    private static DateTimeOffset ResolveLastUpdated(IReadOnlyDictionary<string, string>? map)
    {
        if (map is not null)
        {
            if (map.TryGetValue("last_updated", out var rawLastUpdated)
                && DateTimeOffset.TryParse(rawLastUpdated, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var lastUpdated))
            {
                return lastUpdated;
            }

            if (map.TryGetValue("lastUpdated", out rawLastUpdated)
                && DateTimeOffset.TryParse(rawLastUpdated, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out lastUpdated))
            {
                return lastUpdated;
            }
        }

        return DateTimeOffset.UtcNow;
    }

    private static string? ResolveSource(IReadOnlyDictionary<string, string>? map)
        => map is not null && map.TryGetValue("source", out var source) && !string.IsNullOrWhiteSpace(source)
            ? source
            : null;

    private static string NormalizeNewLines(string? value)
        => value is null
            ? string.Empty
            : value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string NormalizeScalar(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"') && trimmed.Length >= 2)
        {
            return trimmed[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
        }

        if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length >= 2)
        {
            return trimmed[1..^1].Trim();
        }

        return trimmed;
    }

    private static string QuoteScalar(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return "\"\"";
        }

        if (trimmed.IndexOfAny([':', '#']) >= 0 || trimmed.Contains(' '))
        {
            return $"\"{trimmed.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
        }

        return trimmed;
    }
}

internal sealed record ParsedIdentityDocument
{
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, IdentityField> Fields { get; init; } = new Dictionary<string, IdentityField>(StringComparer.OrdinalIgnoreCase);

    public string Body { get; init; } = string.Empty;

    public bool HasMalformedFrontmatter { get; init; }
}

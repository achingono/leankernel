using System.Globalization;

namespace LeanKernel.Logic.Memory;

/// <summary>
/// Parses stored memory page markdown into structured memory models.
/// </summary>
public sealed class MemoryPageParser
{
    /// <summary>
    /// Parses a memory page key and content payload into a structured snapshot.
    /// </summary>
    /// <param name="key">The memory item key.</param>
    /// <param name="content">The stored memory page content.</param>
    /// <returns>The parsed memory page snapshot.</returns>
    public MemoryPageSnapshot Parse(string key, string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var metadata = ExtractMetadata(lines);
        var fields = ParseFields(lines, metadata);
        var factText = ExtractFactText(lines);
        var session = GetMetadata(metadata, "Session");
        var turn = GetMetadata(metadata, "Turn");
        var recordedAt = TryGetTimestamp(metadata, "RecordedAt")
            ?? TryGetTimestamp(metadata, "UpdatedAt")
            ?? DateTimeOffset.UtcNow;
        var supersededBy = GetMetadata(metadata, "SupersededBy");
        var explicitLinks = ParseCsv(GetMetadata(metadata, "Supersedes"));
        var dimensions = ParseDimensions(lines, metadata, fields);
        var links = ParseLinks(lines);
        var isRetired = normalized.TrimStart().StartsWith("# Retired Fact", StringComparison.OrdinalIgnoreCase);

        return new MemoryPageSnapshot(
            key,
            content,
            factText,
            NormalizeFactText(factText),
            recordedAt,
            metadata,
            fields,
            session,
            turn,
            explicitLinks,
            supersededBy,
            dimensions.Primary,
            dimensions.Secondary,
            links,
            isRetired);
    }

    /// <summary>
    /// Extracts metadata entries from list item lines in the page.
    /// </summary>
    /// <param name="lines">The page lines to inspect.</param>
    /// <returns>The extracted metadata dictionary.</returns>
    private static Dictionary<string, string> ExtractMetadata(IEnumerable<string> lines)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (!line.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            var split = line.IndexOf(':');
            if (split <= 2)
            {
                continue;
            }

            var key = line[2..split].Trim();
            var value = line[(split + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                metadata[key] = value;
            }
        }

        return metadata;
    }

    /// <summary>
    /// Parses the 5W1H field values from the page body and metadata.
    /// </summary>
    /// <param name="lines">The page lines to inspect.</param>
    /// <param name="metadata">The previously extracted metadata dictionary.</param>
    /// <returns>The parsed field dictionary.</returns>
    private static IReadOnlyDictionary<string, string?> ParseFields(IReadOnlyList<string> lines, IReadOnlyDictionary<string, string> metadata)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var field in MemoryPageFields.FiveWOneH)
        {
            result[field] = null;
        }

        foreach (var line in lines)
        {
            if (!line.StartsWith("- ", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var field in MemoryPageFields.FiveWOneH)
            {
                var prefix = $"- {field}:";
                if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result[field] = line[prefix.Length..].Trim();
                }
            }
        }

        foreach (var field in MemoryPageFields.FiveWOneH)
        {
            if (string.IsNullOrWhiteSpace(result[field]) && metadata.TryGetValue(field, out var value))
            {
                result[field] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Parses the primary and secondary dimensions assigned to the page.
    /// </summary>
    /// <param name="lines">The page lines to inspect.</param>
    /// <param name="metadata">The metadata dictionary for fallback values.</param>
    /// <param name="fields">The parsed 5W1H field values.</param>
    /// <returns>The parsed primary and secondary dimensions.</returns>
    private static (string Primary, IReadOnlyList<string> Secondary) ParseDimensions(
        IReadOnlyList<string> lines,
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyDictionary<string, string?> fields)
    {
        var primary = "what";
        var secondary = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("- PrimaryDimension:", StringComparison.OrdinalIgnoreCase))
            {
                primary = MemoryPageFields.NormalizeDimension(line.Split(':', 2)[1]);
            }

            if (line.StartsWith("- SecondaryDimensions:", StringComparison.OrdinalIgnoreCase))
            {
                secondary = ParseCsv(line.Split(':', 2)[1])
                    .Select(MemoryPageFields.NormalizeDimension)
                    .Where(d => !d.Equals(primary, StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
        }

        if (secondary.Count == 0)
        {
            secondary = MemoryPageFields.FiveWOneH
                .Select(static v => v.ToLowerInvariant())
                .Where(d => !d.Equals(primary, StringComparison.Ordinal))
                .Where(d =>
                {
                    var pascal = char.ToUpperInvariant(d[0]) + d[1..];
                    return fields.TryGetValue(pascal, out var value) && !string.IsNullOrWhiteSpace(value);
                })
                .ToList();
        }

        if (metadata.TryGetValue("PrimaryDimension", out var primaryMeta))
        {
            primary = MemoryPageFields.NormalizeDimension(primaryMeta);
        }

        return (primary, secondary);
    }

    /// <summary>
    /// Parses explicit related links from the page content.
    /// </summary>
    /// <param name="lines">The page lines to inspect.</param>
    /// <returns>The parsed page links.</returns>
    private static IReadOnlyList<MemoryPageLink> ParseLinks(IReadOnlyList<string> lines)
    {
        var links = new List<MemoryPageLink>();
        foreach (var line in lines)
        {
            if (!line.StartsWith("- Related:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var payload = line[10..].Trim();
            var parts = payload.Split('|', 2);
            var key = parts[0].Trim();
            var reasons = parts.Length > 1
                ? ParseCsv(parts[1])
                : ["explicit-related"];
            if (!string.IsNullOrWhiteSpace(key))
            {
                links.Add(new MemoryPageLink(key, "explicit-related", 0, reasons));
            }
        }

        return links;
    }

    /// <summary>
    /// Extracts the fact text section from the page content.
    /// </summary>
    /// <param name="lines">The page lines to inspect.</param>
    /// <returns>The extracted fact text.</returns>
    private static string ExtractFactText(IReadOnlyList<string> lines)
    {
        var factLines = new List<string>();
        var started = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (!started)
            {
                started = IsFactHeading(trimmed);
                continue;
            }

            if (IsFactSectionBoundary(trimmed))
            {
                if (factLines.Count > 0)
                {
                    break;
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed) && factLines.Count == 0)
            {
                continue;
            }

            factLines.Add(trimmed);
        }

        return string.Join("\n", factLines).Trim();
    }

    /// <summary>
    /// Determines whether the supplied line begins the fact section.
    /// </summary>
    /// <param name="value">The line value to inspect.</param>
    /// <returns><c>true</c> when the line is a fact heading; otherwise, <c>false</c>.</returns>
    private static bool IsFactHeading(string value)
    {
        return value.StartsWith("# Learned Fact", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("# Retired Fact", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether the supplied line ends the fact section.
    /// </summary>
    /// <param name="value">The line value to inspect.</param>
    /// <returns><c>true</c> when the line is a section boundary; otherwise, <c>false</c>.</returns>
    private static bool IsFactSectionBoundary(string value)
    {
        return value.StartsWith("## ", StringComparison.Ordinal)
            || value.StartsWith("- ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Normalizes fact text for similarity comparisons.
    /// </summary>
    /// <param name="factText">The fact text to normalize.</param>
    /// <returns>The normalized fact text.</returns>
    private static string NormalizeFactText(string factText)
    {
        if (string.IsNullOrWhiteSpace(factText))
        {
            return string.Empty;
        }

        return string.Join(" ", factText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }

    /// <summary>
    /// Retrieves a non-empty metadata value when present.
    /// </summary>
    /// <param name="metadata">The metadata dictionary to inspect.</param>
    /// <param name="key">The metadata key to read.</param>
    /// <returns>The metadata value, or <c>null</c> when not present.</returns>
    private static string? GetMetadata(IReadOnlyDictionary<string, string> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    /// <summary>
    /// Tries to parse a metadata timestamp using invariant UTC semantics.
    /// </summary>
    /// <param name="metadata">The metadata dictionary to inspect.</param>
    /// <param name="key">The metadata key to parse.</param>
    /// <returns>The parsed timestamp, or <c>null</c> when parsing fails.</returns>
    private static DateTimeOffset? TryGetTimestamp(IReadOnlyDictionary<string, string> metadata, string key)
    {
        var raw = GetMetadata(metadata, key);
        if (raw is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// Parses a comma-separated list into distinct trimmed values.
    /// </summary>
    /// <param name="value">The comma-separated value to parse.</param>
    /// <returns>The parsed values.</returns>
    private static List<string> ParseCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

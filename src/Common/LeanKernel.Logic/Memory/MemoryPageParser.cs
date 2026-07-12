using System.Globalization;

namespace LeanKernel.Logic.Memory;

public sealed class MemoryPageParser
{
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

    private static string ExtractFactText(IReadOnlyList<string> lines)
    {
        var factLines = new List<string>();
        var started = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (!started)
            {
                if (trimmed.StartsWith("# Learned Fact", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("# Retired Fact", StringComparison.OrdinalIgnoreCase))
                {
                    started = true;
                }

                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal) || trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                if (factLines.Count > 0)
                {
                    break;
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(trimmed) || factLines.Count > 0)
            {
                factLines.Add(trimmed);
            }
        }

        return string.Join("\n", factLines).Trim();
    }

    private static string NormalizeFactText(string factText)
    {
        if (string.IsNullOrWhiteSpace(factText))
        {
            return string.Empty;
        }

        return string.Join(" ", factText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }

    private static string? GetMetadata(IReadOnlyDictionary<string, string> metadata, string key)
    {
        return metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

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

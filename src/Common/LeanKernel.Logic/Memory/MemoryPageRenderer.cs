using System.Globalization;
using System.Text;

namespace LeanKernel.Logic.Memory;

public sealed class MemoryPageRenderer
{
    public string RenderLearnedPage(
        IReadOnlyDictionary<string, string?> fields,
        string primaryDimension,
        IReadOnlyList<string> secondaryDimensions,
        IReadOnlyList<MemoryPageLink> links,
        string normalizationStatus,
        string normalizationMethod,
        IReadOnlyList<string> missingFields,
        string? session,
        string? turn,
        DateTimeOffset? recordedAt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Learned Fact");
        builder.AppendLine();
        builder.AppendLine("## 5W1H");
        builder.AppendLine();
        foreach (var field in MemoryPageFields.FiveWOneH)
        {
            fields.TryGetValue(field, out var value);
            builder.AppendLine($"- {field}: {value ?? string.Empty}");
        }

        builder.AppendLine();
        builder.AppendLine("## Dimensions");
        builder.AppendLine();
        builder.AppendLine($"- PrimaryDimension: {MemoryPageFields.NormalizeDimension(primaryDimension)}");
        builder.AppendLine($"- SecondaryDimensions: {string.Join(", ", secondaryDimensions)}");

        builder.AppendLine();
        builder.AppendLine("## Links");
        builder.AppendLine();
        foreach (var link in links)
        {
            builder.AppendLine($"- Related: {link.TargetKey} | {string.Join(", ", link.Reasons)}");
        }

        builder.AppendLine();
        builder.AppendLine("## Normalization");
        builder.AppendLine();
        builder.AppendLine($"- NormalizationStatus: {normalizationStatus}");
        builder.AppendLine($"- NormalizationMethod: {normalizationMethod}");
        builder.AppendLine($"- Missing5W1H: {(missingFields.Count == 0 ? string.Empty : string.Join(", ", missingFields))}");
        AppendIfNotEmpty(builder, "Session", session);
        AppendIfNotEmpty(builder, "Turn", turn);
        if (recordedAt.HasValue)
        {
            builder.AppendLine($"- RecordedAt: {recordedAt.Value.ToString("O", CultureInfo.InvariantCulture)}");
        }

        return builder.ToString().TrimEnd();
    }

    public string RenderSeedPage(string factText, string? sessionId, string? turnId, DateTimeOffset recordedAt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Learned Fact");
        builder.AppendLine();
        builder.AppendLine(factText.Trim());
        builder.AppendLine();
        AppendIfNotEmpty(builder, "Session", sessionId);
        AppendIfNotEmpty(builder, "Turn", turnId);
        builder.AppendLine($"- RecordedAt: {recordedAt:O}");
        return builder.ToString().TrimEnd();
    }

    private static void AppendIfNotEmpty(StringBuilder builder, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"- {key}: {value}");
        }
    }
}

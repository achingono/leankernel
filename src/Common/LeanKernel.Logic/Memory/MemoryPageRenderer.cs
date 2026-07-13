using System.Globalization;
using System.Text;

namespace LeanKernel.Logic.Memory;

/// <summary>
/// Renders learned and seed memory pages using the markdown format stored by the memory provider.
/// </summary>
public sealed class MemoryPageRenderer
{
    /// <summary>
    /// Renders a normalized learned memory page.
    /// </summary>
    /// <param name="fields">The normalized 5W1H fields.</param>
    /// <param name="primaryDimension">The primary dimension assigned to the page.</param>
    /// <param name="secondaryDimensions">The secondary dimensions assigned to the page.</param>
    /// <param name="links">The related page links to include.</param>
    /// <param name="normalizationStatus">The normalization completeness status.</param>
    /// <param name="normalizationMethod">The method used to normalize the page.</param>
    /// <param name="missingFields">The missing 5W1H fields.</param>
    /// <param name="session">The optional session identifier.</param>
    /// <param name="turn">The optional turn identifier.</param>
    /// <param name="recordedAt">The optional recorded-at timestamp.</param>
    /// <returns>The rendered memory page content.</returns>
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

    /// <summary>
    /// Renders a seed page from raw fact text before normalization.
    /// </summary>
    /// <param name="factText">The fact text to render.</param>
    /// <param name="sessionId">The optional session identifier.</param>
    /// <param name="turnId">The optional turn identifier.</param>
    /// <param name="recordedAt">The timestamp associated with the fact.</param>
    /// <returns>The rendered seed page content.</returns>
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

    /// <summary>
    /// Appends a metadata line when the provided value is not empty.
    /// </summary>
    /// <param name="builder">The string builder to append to.</param>
    /// <param name="key">The metadata key to render.</param>
    /// <param name="value">The metadata value to render.</param>
    private static void AppendIfNotEmpty(StringBuilder builder, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"- {key}: {value}");
        }
    }
}

using System.Globalization;
using System.Text;

namespace LeanKernel.Logic.Memory;

/// <summary>
/// Parameters for rendering a learned memory page.
/// </summary>
public sealed record LearnedPageParameters(
    IReadOnlyDictionary<string, string?> Fields,
    string PrimaryDimension,
    IReadOnlyList<string> SecondaryDimensions,
    IReadOnlyList<MemoryPageLink> Links,
    string NormalizationStatus,
    string NormalizationMethod,
    IReadOnlyList<string> MissingFields,
    string? Session,
    string? Turn,
    DateTimeOffset? RecordedAt);

/// <summary>
/// Renders learned and seed memory pages using the markdown format stored by the memory provider.
/// </summary>
public sealed class MemoryPageRenderer
{
    /// <summary>
    /// Renders a normalized learned memory page.
    /// </summary>
    /// <param name="parameters">The learned page parameters.</param>
    /// <returns>The rendered memory page content.</returns>
    public string RenderLearnedPage(LearnedPageParameters parameters)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Learned Fact");
        builder.AppendLine();
        builder.AppendLine("## 5W1H");
        builder.AppendLine();
        foreach (var field in MemoryPageFields.FiveWOneH)
        {
            parameters.Fields.TryGetValue(field, out var value);
            builder.AppendLine($"- {field}: {value ?? string.Empty}");
        }

        builder.AppendLine();
        builder.AppendLine("## Dimensions");
        builder.AppendLine();
        builder.AppendLine($"- PrimaryDimension: {MemoryPageFields.NormalizeDimension(parameters.PrimaryDimension)}");
        builder.AppendLine($"- SecondaryDimensions: {string.Join(", ", parameters.SecondaryDimensions)}");

        builder.AppendLine();
        builder.AppendLine("## Links");
        builder.AppendLine();
        foreach (var link in parameters.Links)
        {
            builder.AppendLine($"- Related: {link.TargetKey} | {string.Join(", ", link.Reasons)}");
        }

        builder.AppendLine();
        builder.AppendLine("## Normalization");
        builder.AppendLine();
        builder.AppendLine($"- NormalizationStatus: {parameters.NormalizationStatus}");
        builder.AppendLine($"- NormalizationMethod: {parameters.NormalizationMethod}");
        builder.AppendLine($"- Missing5W1H: {(parameters.MissingFields.Count == 0 ? string.Empty : string.Join(", ", parameters.MissingFields))}");
        AppendIfNotEmpty(builder, "Session", parameters.Session);
        AppendIfNotEmpty(builder, "Turn", parameters.Turn);
        if (parameters.RecordedAt.HasValue)
        {
            builder.AppendLine($"- RecordedAt: {parameters.RecordedAt.Value.ToString("O", CultureInfo.InvariantCulture)}");
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
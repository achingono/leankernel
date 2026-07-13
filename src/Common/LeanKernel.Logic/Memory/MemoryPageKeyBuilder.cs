using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LeanKernel.Logic.Memory;

/// <summary>
/// Builds stable scope-relative keys for normalized memory pages.
/// </summary>
public sealed partial class MemoryPageKeyBuilder
{
    private static readonly HashSet<string> GenericSlugs =
        ["action", "fact", "event", "unknown"];

    /// <summary>
    /// Builds a scope-relative memory key from the primary dimension, subject, fact text, and timestamp.
    /// </summary>
    /// <param name="primaryDimension">The normalized primary dimension for the fact.</param>
    /// <param name="subjectValue">The subject value associated with the primary dimension.</param>
    /// <param name="factText">The learned fact text.</param>
    /// <param name="recordedAt">The timestamp used to generate a stable fact identifier.</param>
    /// <returns>The scope-relative memory key.</returns>
    public string BuildScopeRelativeKey(
        string primaryDimension,
        string? subjectValue,
        string factText,
        DateTimeOffset recordedAt)
    {
        var dim = MemoryPageFields.NormalizeDimension(primaryDimension);
        var factId = BuildFactId(factText, recordedAt);
        var slug = BuildSubjectSlug(subjectValue, factId);
        return $"facts/{dim}/{slug}/{factId}";
    }

    /// <summary>
    /// Builds a normalized subject slug for inclusion in a memory key.
    /// </summary>
    /// <param name="value">The subject value to normalize.</param>
    /// <param name="factId">The generated fact identifier used as a fallback.</param>
    /// <returns>A normalized slug suitable for use in a memory key.</returns>
    public string BuildSubjectSlug(string? value, string factId)
    {
        var slug = SlugRegex().Replace((value ?? string.Empty).Trim().ToLowerInvariant(), "-")
            .Trim('-');

        if (slug.Length > 64)
        {
            slug = slug[..64].Trim('-');
        }

        if (string.IsNullOrWhiteSpace(slug) || GenericSlugs.Contains(slug))
        {
            return $"fact-{factId[..Math.Min(8, factId.Length)]}";
        }

        return slug;
    }

    /// <summary>
    /// Builds a deterministic fact identifier from the fact text and timestamp.
    /// </summary>
    /// <param name="factText">The fact text to hash.</param>
    /// <param name="recordedAt">The timestamp to include in the hash input.</param>
    /// <returns>The generated fact identifier.</returns>
    private static string BuildFactId(string factText, DateTimeOffset recordedAt)
    {
        var input = $"{factText.Trim()}|{recordedAt:O}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>
    /// Gets the regex used to collapse non-alphanumeric characters in slugs.
    /// </summary>
    /// <returns>The compiled slug normalization regex.</returns>
    [GeneratedRegex("[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex SlugRegex();
}

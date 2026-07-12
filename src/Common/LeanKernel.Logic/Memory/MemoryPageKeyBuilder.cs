using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LeanKernel.Logic.Memory;

public sealed partial class MemoryPageKeyBuilder
{
    private static readonly HashSet<string> GenericSlugs =
        ["action", "fact", "event", "unknown"];

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

    private static string BuildFactId(string factText, DateTimeOffset recordedAt)
    {
        var input = $"{factText.Trim()}|{recordedAt:O}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex SlugRegex();
}

using System.Text.RegularExpressions;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Agents.Enhancement;

internal static partial class EnhancementTextMatcher
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a",
        "an",
        "and",
        "are",
        "as",
        "at",
        "be",
        "by",
        "for",
        "from",
        "how",
        "i",
        "in",
        "is",
        "it",
        "of",
        "on",
        "or",
        "that",
        "the",
        "this",
        "to",
        "was",
        "were",
        "what",
        "when",
        "where",
        "who",
        "why",
        "with",
        "you",
        "your"
    };

    internal static IReadOnlyList<RetrievalCandidate> FindRelevantCandidates(
        string response,
        IReadOnlyList<RetrievalCandidate>? candidates,
        int maxResults = 3)
    {
        if (string.IsNullOrWhiteSpace(response) || candidates is null || candidates.Count == 0)
        {
            return [];
        }

        return candidates
            .Where(candidate => IsRelevant(response, candidate))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Key, StringComparer.Ordinal)
            .Take(maxResults)
            .ToArray();
    }

    internal static bool IsRelevant(string response, RetrievalCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        if (ContainsIgnoreCase(response, candidate.Key) || ContainsIgnoreCase(response, ResolveCitationKey(candidate)))
        {
            return true;
        }

        var responseKeywords = ExtractKeywords(response);
        if (responseKeywords.Count == 0)
        {
            return false;
        }

        var candidateKeywords = ExtractKeywords(BuildCandidateText(candidate));
        var overlap = responseKeywords.Intersect(candidateKeywords, StringComparer.Ordinal).Count();
        return overlap >= 2 || (overlap >= 1 && candidate.Score >= 0.85);
    }

    internal static string ResolveCitationKey(RetrievalCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (TryGetMetadataValue(candidate.Metadata, "page_key", out var pageKey)
            || TryGetMetadataValue(candidate.Metadata, "pageKey", out pageKey)
            || TryGetMetadataValue(candidate.Metadata, "id", out pageKey))
        {
            return pageKey;
        }

        return candidate.Key;
    }

    internal static IReadOnlySet<string> ExtractKeywords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return TokenRegex()
            .Matches(text)
            .Select(static match => match.Value.ToLowerInvariant())
            .Where(token => token.Length >= 4 && !StopWords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string BuildCandidateText(RetrievalCandidate candidate)
    {
        var parts = new List<string>
        {
            candidate.Key,
            candidate.Content,
            ResolveCitationKey(candidate)
        };

        if (TryGetMetadataValue(candidate.Metadata, "title", out var title))
        {
            parts.Add(title);
        }

        return string.Join(' ', parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static bool ContainsIgnoreCase(string source, string value)
        => !string.IsNullOrWhiteSpace(value)
            && source.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetMetadataValue(
        IReadOnlyDictionary<string, string>? metadata,
        string key,
        out string value)
    {
        value = string.Empty;

        if (metadata is null || !metadata.TryGetValue(key, out var metadataValue) || string.IsNullOrWhiteSpace(metadataValue))
        {
            return false;
        }

        value = metadataValue.Trim();
        return true;
    }

    [GeneratedRegex("[A-Za-z0-9_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}

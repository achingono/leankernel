using System.Diagnostics.Metrics;
using System.Text;

namespace LeanKernel.Logic.Memory;

/// <summary>
/// Builds deterministic links between related memory pages.
/// </summary>
public sealed class MemoryPageLinker
{
    private static readonly Meter Meter = new("LeanKernel.Logic.Memory", "1.0.0");
    private static readonly Histogram<int> LinkCountHistogram = Meter.CreateHistogram<int>("memory.links.count");

    /// <summary>
    /// Builds related links for a target page from a candidate set.
    /// </summary>
    /// <param name="target">The target page to link from.</param>
    /// <param name="candidates">The candidate pages to score.</param>
    /// <param name="fields">The target page fields.</param>
    /// <param name="primaryDimension">The target page primary dimension.</param>
    /// <param name="secondaryDimensions">The target page secondary dimensions.</param>
    /// <returns>The highest scoring related links.</returns>
    public IReadOnlyList<MemoryPageLink> BuildLinks(
        MemoryPageSnapshot target,
        IReadOnlyList<MemoryPageSnapshot> candidates,
        IReadOnlyDictionary<string, string?> fields,
        string primaryDimension,
        IReadOnlyList<string> secondaryDimensions)
    {
        var scored = new Dictionary<string, (int Score, HashSet<string> Reasons)>(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            var scoredCandidate = ScoreCandidate(target, candidate, primaryDimension, secondaryDimensions);
            if (scoredCandidate is null)
            {
                continue;
            }

            scored[candidate.Key] = scoredCandidate.Value;
        }

        var links = scored
            .OrderByDescending(static x => x.Value.Score)
            .ThenBy(static x => x.Key, StringComparer.Ordinal)
            .Take(12)
            .Select(entry => new MemoryPageLink(
                entry.Key,
                entry.Value.Reasons.Contains("supersedes") ? "supersedes" : "related",
                entry.Value.Score,
                entry.Value.Reasons.ToList()))
            .ToList();

        LinkCountHistogram.Record(links.Count);
        return links;
    }

    /// <summary>
    /// Scores a single candidate page for potential linkage.
    /// </summary>
    /// <param name="target">The target page.</param>
    /// <param name="candidate">The candidate page.</param>
    /// <param name="primaryDimension">The target primary dimension.</param>
    /// <param name="secondaryDimensions">The target secondary dimensions.</param>
    /// <returns>The candidate score and reasons, or <c>null</c> when the candidate should be excluded.</returns>
    private static (int Score, HashSet<string> Reasons)? ScoreCandidate(
        MemoryPageSnapshot target,
        MemoryPageSnapshot candidate,
        string primaryDimension,
        IReadOnlyList<string> secondaryDimensions)
    {
        if (candidate.Key.Equals(target.Key, StringComparison.Ordinal))
        {
            return null;
        }

        var score = 0;
        var reasons = new HashSet<string>(StringComparer.Ordinal);

        score += ScoreExplicitRelationship(target, candidate, reasons);
        score += ScoreSessionRelationship(target, candidate, reasons);
        score += ScoreSimilarity(target.NormalizedFactText, candidate.NormalizedFactText, reasons);
        score += ScoreDimensionRelationship(candidate.PrimaryDimension, primaryDimension, secondaryDimensions, reasons);

        return score > 0 ? (score, reasons) : null;
    }

    /// <summary>
    /// Scores explicit cross-page relationships.
    /// </summary>
    private static int ScoreExplicitRelationship(MemoryPageSnapshot target, MemoryPageSnapshot candidate, ISet<string> reasons)
    {
        if (!target.ExplicitLinks.Contains(candidate.Key, StringComparer.Ordinal)
            && !candidate.ExplicitLinks.Contains(target.Key, StringComparer.Ordinal)
            && !string.Equals(target.SupersededBy, candidate.Key, StringComparison.Ordinal)
            && !string.Equals(candidate.SupersededBy, target.Key, StringComparison.Ordinal))
        {
            return 0;
        }

        reasons.Add("explicit-related");
        return 100;
    }

    /// <summary>
    /// Scores whether two pages belong to the same session or turn.
    /// </summary>
    private static int ScoreSessionRelationship(MemoryPageSnapshot target, MemoryPageSnapshot candidate, ISet<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(target.SessionId)
            || !target.SessionId.Equals(candidate.SessionId, StringComparison.Ordinal))
        {
            return 0;
        }

        var score = 70;
        reasons.Add("same-session");

        if (!string.IsNullOrWhiteSpace(target.TurnId)
            && target.TurnId.Equals(candidate.TurnId, StringComparison.Ordinal))
        {
            score += 20;
            reasons.Add("same-turn");
        }

        return score;
    }

    /// <summary>
    /// Scores the lexical similarity between two normalized fact strings.
    /// </summary>
    private static int ScoreSimilarity(string left, string right, ISet<string> reasons)
    {
        var similarity = Similarity(left, right);
        if (similarity <= 0.2)
        {
            return 0;
        }

        reasons.Add("semantic-related");
        return (int)Math.Round(similarity * 30, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Scores whether the candidate dimensions align with the target dimensions.
    /// </summary>
    private static int ScoreDimensionRelationship(
        string candidatePrimary,
        string primaryDimension,
        IReadOnlyList<string> secondaryDimensions,
        ISet<string> reasons)
    {
        var score = 0;

        if (candidatePrimary.Equals(primaryDimension, StringComparison.Ordinal))
        {
            score += 30;
            reasons.Add("same-dimension");
        }

        if (secondaryDimensions.Contains(candidatePrimary, StringComparer.Ordinal))
        {
            score += 10;
            reasons.Add("same-subject");
        }

        return score;
    }

    /// <summary>
    /// Computes a Jaccard-style similarity score over tokenized fact text.
    /// </summary>
    private static double Similarity(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0;
        }

        var leftTokens = Tokens(left);
        var rightTokens = Tokens(right);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0;
        }

        var intersectionCount = leftTokens.Intersect(rightTokens).Count();
        var unionCount = leftTokens.Union(rightTokens).Count();
        return unionCount == 0 ? 0 : (double)intersectionCount / unionCount;
    }

    /// <summary>
    /// Tokenizes a string into lowercase alphanumeric terms.
    /// </summary>
    private static HashSet<string> Tokens(string value)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var current = new StringBuilder();
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(char.ToLowerInvariant(ch));
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
using System.Diagnostics.Metrics;
using System.Text;

namespace LeanKernel.Logic.Memory;

public sealed class MemoryPageLinker
{
    private static readonly Meter Meter = new("LeanKernel.Logic.Memory", "1.0.0");
    private static readonly Histogram<int> LinkCountHistogram = Meter.CreateHistogram<int>("memory.links.count");

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
            if (candidate.Key.Equals(target.Key, StringComparison.Ordinal))
            {
                continue;
            }

            var score = 0;
            var reasons = new HashSet<string>(StringComparer.Ordinal);

            if (target.ExplicitLinks.Contains(candidate.Key, StringComparer.Ordinal)
                || candidate.ExplicitLinks.Contains(target.Key, StringComparer.Ordinal)
                || string.Equals(target.SupersededBy, candidate.Key, StringComparison.Ordinal)
                || string.Equals(candidate.SupersededBy, target.Key, StringComparison.Ordinal))
            {
                score += 100;
                reasons.Add("explicit-related");
            }

            if (!string.IsNullOrWhiteSpace(target.SessionId)
                && target.SessionId.Equals(candidate.SessionId, StringComparison.Ordinal))
            {
                score += 70;
                reasons.Add("same-session");

                if (!string.IsNullOrWhiteSpace(target.TurnId)
                    && target.TurnId.Equals(candidate.TurnId, StringComparison.Ordinal))
                {
                    score += 20;
                    reasons.Add("same-turn");
                }
            }

            var similarity = Similarity(target.NormalizedFactText, candidate.NormalizedFactText);
            if (similarity > 0.2)
            {
                score += (int)Math.Round(similarity * 30, MidpointRounding.AwayFromZero);
                reasons.Add("semantic-related");
            }

            var candidatePrimary = candidate.PrimaryDimension;
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

            if (score > 0)
            {
                scored[candidate.Key] = (score, reasons);
            }
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

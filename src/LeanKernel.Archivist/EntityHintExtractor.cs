using System.Text.RegularExpressions;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist;

public enum EntityHintType
{
    Person,
    Organization
}

public sealed record EntityHint
{
    public required string NormalizedName { get; init; }
    public required EntityHintType Type { get; init; }
    public double Confidence { get; init; }
}

/// <summary>
/// Extracts lightweight entity hints from the current query and recent history.
/// </summary>
internal sealed partial class EntityHintExtractor
{
    private static readonly HashSet<string> Pronouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "he", "him", "his", "she", "her", "hers", "they", "them", "their"
    };

    private static readonly HashSet<string> RelationshipTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "mother", "father", "mom", "mum", "dad", "parent", "parents",
        "brother", "sister", "sibling", "siblings",
        "wife", "husband", "partner",
        "son", "daughter", "child", "children", "kids", "family"
    };

    private static readonly HashSet<string> NameStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "I", "Im", "I'm", "The", "A", "An", "And", "Or", "But", "So", "If", "When", "Where", "Why", "How", "What",
        "Should", "Can", "Will", "Could", "Would", "Do", "Did", "Does", "Is", "Are", "Am", "Tell"
    };

    [GeneratedRegex(@"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+){0,2}\b", RegexOptions.Compiled)]
    private static partial Regex PersonNameRegex();

    [GeneratedRegex(@"\b[A-Z]{2,}\b", RegexOptions.Compiled)]
    private static partial Regex AcronymRegex();

    [GeneratedRegex(@"\b(?:at|from|with)\s+(?<org>[A-Z][A-Za-z0-9&'.-]*(?:\s+[A-Z][A-Za-z0-9&'.-]*){0,4})\b", RegexOptions.Compiled)]
    private static partial Regex OrganizationPhraseRegex();

    /// <summary>
    /// Extract entity hints from query + recent turns.
    /// </summary>
    public IReadOnlyList<EntityHint> Extract(string queryText, IReadOnlyList<ConversationTurn> recentHistory)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return [];
        }

        var hints = new List<EntityHint>();
        var lowered = queryText.ToLowerInvariant();

        foreach (var organization in ExtractOrganizations(queryText))
        {
            hints.Add(new EntityHint
            {
                NormalizedName = organization,
                Type = EntityHintType.Organization,
                Confidence = 0.9
            });
        }

        var explicitPeople = ExtractPeople(queryText);
        foreach (var person in explicitPeople)
        {
            hints.Add(new EntityHint
            {
                NormalizedName = person,
                Type = EntityHintType.Person,
                Confidence = 1.0
            });
        }

        foreach (var relationship in ExtractRelationshipHints(lowered))
        {
            hints.Add(new EntityHint
            {
                NormalizedName = relationship,
                Type = EntityHintType.Person,
                Confidence = 0.85
            });
        }

        if (!hints.Any(h => h.Type == EntityHintType.Person) && ContainsPronoun(lowered))
        {
            var historicalPerson = ResolveRecentPerson(recentHistory);
            if (!string.IsNullOrWhiteSpace(historicalPerson))
            {
                hints.Add(new EntityHint
                {
                    NormalizedName = historicalPerson,
                    Type = EntityHintType.Person,
                    Confidence = 0.7
                });
            }
        }

        return hints
            .GroupBy(h => $"{h.Type}:{h.NormalizedName}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Confidence).First())
            .ToList();
    }

    private static IEnumerable<string> ExtractPeople(string text)
    {
        foreach (Match match in PersonNameRegex().Matches(text))
        {
            var candidate = match.Value.Trim();
            var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (NameStopWords.Contains(candidate) || words.Any(word => NameStopWords.Contains(word)))
            {
                continue;
            }

            yield return candidate.ToLowerInvariant();
        }
    }

    private static IEnumerable<string> ExtractOrganizations(string text)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in AcronymRegex().Matches(text))
        {
            var candidate = match.Value.Trim();
            if (candidate.Length >= 2)
            {
                found.Add(candidate.ToLowerInvariant());
            }
        }

        foreach (Match match in OrganizationPhraseRegex().Matches(text))
        {
            var candidate = match.Groups["org"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                found.Add(candidate.ToLowerInvariant());
            }
        }

        return found;
    }

    private static IEnumerable<string> ExtractRelationshipHints(string loweredText)
    {
        var tokens = loweredText
            .Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries);

        return tokens.Where(token => RelationshipTerms.Contains(token));
    }

    private static bool ContainsPronoun(string loweredText)
    {
        var tokens = loweredText
            .Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '/', '\\', '-', '_'],
                StringSplitOptions.RemoveEmptyEntries);

        return tokens.Any(token => Pronouns.Contains(token));
    }

    private static string? ResolveRecentPerson(IReadOnlyList<ConversationTurn> recentHistory)
    {
        foreach (var turn in recentHistory.Reverse())
        {
            var people = ExtractPeople(turn.Content).ToList();
            if (people.Count > 0)
            {
                return people[0];
            }
        }

        return null;
    }
}

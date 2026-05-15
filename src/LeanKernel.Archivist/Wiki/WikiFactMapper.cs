using System.Text.RegularExpressions;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Wiki;

/// <summary>
/// Maps extracted wiki DTOs into canonical wiki entries and facts.
/// </summary>
public sealed class WikiFactMapper
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "is", "are", "was", "were", "to", "of", "and", "or", "in", "on", "for", "with", "by", "at"
    };

    /// <summary>
    /// Maps extracted facts into canonical wiki entries grouped by entry id.
    /// </summary>
    public IReadOnlyList<WikiEntry> Map(IReadOnlyList<ExtractedWikiFact> extractedFacts, string sourceId)
    {
        if (extractedFacts.Count == 0)
            return [];

        var entriesById = new Dictionary<string, WikiEntry>(StringComparer.OrdinalIgnoreCase);
        var slugSubjectsByDimension = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var extracted in extractedFacts)
        {
            if (!TryParseDimension(extracted.PrimaryDimension, out var dimension))
                continue;

            var subject = extracted.Subject.Trim();
            var subjectSlug = Slugify(subject);
            if (string.IsNullOrWhiteSpace(subjectSlug))
                continue;

            var canonicalEntryId = BuildCanonicalEntryId(
                dimension,
                subject,
                subjectSlug,
                slugSubjectsByDimension);

            var normalizedClaim = NormalizeClaim(extracted.Claim);
            if (string.IsNullOrWhiteSpace(normalizedClaim))
                continue;

            var fact = new WikiFact
            {
                Claim = extracted.Claim.Trim(),
                Context = new WikiFactContext
                {
                    Who = NormalizeOptional(extracted.Who),
                    What = NormalizeOptional(extracted.What),
                    When = NormalizeOptional(extracted.When),
                    Where = NormalizeOptional(extracted.Where),
                    Why = NormalizeOptional(extracted.Why),
                    How = NormalizeOptional(extracted.How)
                },
                SourceQuote = NormalizeOptional(extracted.SourceQuote),
                NormalizedKey = $"{canonicalEntryId}|{normalizedClaim}",
                Confidence = ComputeConfidence(sourceId, extracted),
                Source = sourceId,
                LastConfirmed = DateTimeOffset.UtcNow,
                EstimatedTokens = (int)Math.Ceiling(extracted.Claim.Length / 4.0),
                Tags = (extracted.Tags ?? [])
                    .Where(static t => !string.IsNullOrWhiteSpace(t))
                    .Select(static t => t.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };

            if (!entriesById.TryGetValue(canonicalEntryId, out var existingEntry))
            {
                entriesById[canonicalEntryId] = new WikiEntry
                {
                    Id = canonicalEntryId,
                    Dimension = dimension,
                    Subject = subject,
                    Summary = NormalizeOptional(extracted.SummaryHint),
                    Facts = [fact],
                    Aliases = ExtractAliases(subject, extracted.Aliases ?? []),
                    Tags = fact.Tags.ToList(),
                    LastAccessed = DateTimeOffset.UtcNow
                };
                continue;
            }

            entriesById[canonicalEntryId] = MergeFact(existingEntry, fact, extracted);
        }

        return entriesById.Values.ToList();
    }

    public static string NormalizeClaim(string claim)
    {
        var lowered = claim.ToLowerInvariant();
        lowered = Regex.Replace(lowered, @"[^\w\s]", " ");
        var tokens = lowered
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !StopWords.Contains(token));
        return string.Join(' ', tokens);
    }

    public static string Slugify(string text) =>
        Regex.Replace(text.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-").Trim('-');

    private static bool TryParseDimension(string? value, out WikiDimension dimension) =>
        Enum.TryParse(value, ignoreCase: true, out dimension);

    private static string BuildCanonicalEntryId(
        WikiDimension dimension,
        string subject,
        string subjectSlug,
        Dictionary<string, HashSet<string>> slugSubjectsByDimension)
    {
        var dimensionKey = dimension.ToString().ToLowerInvariant();
        var key = $"{dimensionKey}:{subjectSlug}";
        if (!slugSubjectsByDimension.TryGetValue(key, out var seenSubjects))
        {
            slugSubjectsByDimension[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                subject
            };
            return $"{dimensionKey}-{subjectSlug}";
        }

        if (seenSubjects.Contains(subject))
            return $"{dimensionKey}-{subjectSlug}";

        seenSubjects.Add(subject);
        return $"{dimensionKey}-{subjectSlug}-{seenSubjects.Count}";
    }

    private static WikiEntry MergeFact(WikiEntry entry, WikiFact incomingFact, ExtractedWikiFact extracted)
    {
        var facts = entry.Facts.ToList();
        var existingIndex = facts.FindIndex(f =>
            string.Equals(f.NormalizedKey, incomingFact.NormalizedKey, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            var existing = facts[existingIndex];
            facts[existingIndex] = existing with
            {
                Confidence = Math.Max(existing.Confidence, incomingFact.Confidence),
                LastConfirmed = DateTimeOffset.UtcNow,
                Source = incomingFact.Source,
                SourceQuote = incomingFact.SourceQuote ?? existing.SourceQuote,
                Tags = existing.Tags.Union(incomingFact.Tags, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }
        else
        {
            facts.Add(incomingFact);
        }

        return entry with
        {
            Facts = facts,
            Summary = string.IsNullOrWhiteSpace(entry.Summary) ? NormalizeOptional(extracted.SummaryHint) : entry.Summary,
            Aliases = entry.Aliases.Union(ExtractAliases(entry.Subject, extracted.Aliases ?? []), StringComparer.OrdinalIgnoreCase).ToList(),
            Tags = entry.Tags.Union(incomingFact.Tags, StringComparer.OrdinalIgnoreCase).ToList(),
            LastAccessed = DateTimeOffset.UtcNow
        };
    }

    private static List<string> ExtractAliases(string subject, IEnumerable<string> aliases)
    {
        var normalizedSubject = subject.Trim();
        return aliases
            .Where(static alias => !string.IsNullOrWhiteSpace(alias))
            .Select(static alias => alias.Trim())
            .Where(alias => !string.Equals(alias, normalizedSubject, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double ComputeConfidence(string sourceId, ExtractedWikiFact fact)
    {
        var baseScore = sourceId.StartsWith("conversation:", StringComparison.OrdinalIgnoreCase) ? 0.85
            : sourceId.StartsWith("scrub:", StringComparison.OrdinalIgnoreCase) ? 0.75
            : sourceId.StartsWith("session:", StringComparison.OrdinalIgnoreCase) ? 0.65
            : 0.60;

        if (!string.IsNullOrWhiteSpace(fact.SourceQuote))
            baseScore += 0.1;

        if (fact.Claim.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 5)
            baseScore -= 0.08;

        return Math.Clamp(baseScore, 0.0, 1.0);
    }
}

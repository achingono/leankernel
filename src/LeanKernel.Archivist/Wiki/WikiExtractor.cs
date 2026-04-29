using LeanKernel.Core.Enums;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Wiki;

/// <summary>
/// Extracts 5W1H structured facts from raw text (LLM responses,
/// user messages, or ingested documents). Uses heuristic patterns
/// for fast extraction; future: delegate to LLM for complex text.
/// </summary>
public static class WikiExtractor
{
    /// <summary>
    /// Extract candidate wiki entries from a conversation exchange.
    /// Returns entries grouped by detected dimension and subject.
    /// </summary>
    public static List<WikiEntry> ExtractFacts(string userMessage, string assistantResponse, string sourceId)
    {
        var entries = new List<WikiEntry>();
        var combined = $"{userMessage}\n{assistantResponse}";

        // Extract entity mentions (Who)
        var whoFacts = ExtractWhoFacts(combined, sourceId);
        entries.AddRange(whoFacts);

        // Extract events/actions (What)
        var whatFacts = ExtractWhatFacts(combined, sourceId);
        entries.AddRange(whatFacts);

        // Extract temporal references (When)
        var whenFacts = ExtractWhenFacts(combined, sourceId);
        entries.AddRange(whenFacts);

        return entries;
    }

    private static List<WikiEntry> ExtractWhoFacts(string text, string sourceId)
    {
        var entries = new List<WikiEntry>();
        var patterns = new[]
        {
            // "X is a/an Y" pattern
            @"(\b[A-Z][a-z]+(?:\s[A-Z][a-z]+)*)\s+is\s+(?:a|an)\s+(.+?)(?:[\.\,\;\n]|$)",
            // "X works at Y" pattern
            @"(\b[A-Z][a-z]+(?:\s[A-Z][a-z]+)*)\s+works?\s+(?:at|for)\s+(.+?)(?:[\.\,\;\n]|$)"
        };

        foreach (var pattern in patterns)
        {
            foreach (System.Text.RegularExpressions.Match match in
                System.Text.RegularExpressions.Regex.Matches(text, pattern))
            {
                var subject = match.Groups[1].Value.Trim();
                var claim = match.Groups[0].Value.Trim().TrimEnd('.', ',', ';', '\n', '\r');

                if (subject.Length < 2 || IsCommonWord(subject)) continue;

                var id = $"who-{Slugify(subject)}";
                var existing = entries.Find(e => e.Id == id);
                if (existing is not null)
                {
                    existing.Facts.Add(CreateFact(claim, sourceId));
                }
                else
                {
                    entries.Add(new WikiEntry
                    {
                        Id = id,
                        Dimension = WikiDimension.Who,
                        Subject = subject,
                        Facts = [CreateFact(claim, sourceId)]
                    });
                }
            }
        }

        return entries;
    }

    private static List<WikiEntry> ExtractWhatFacts(string text, string sourceId)
    {
        var entries = new List<WikiEntry>();

        // Look for "the X project/task/meeting" patterns
        var pattern = @"the\s+(\w+(?:\s+\w+)?)\s+(project|task|meeting|event|issue|feature|bug)\b";
        foreach (System.Text.RegularExpressions.Match match in
            System.Text.RegularExpressions.Regex.Matches(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var subject = $"{match.Groups[1].Value} {match.Groups[2].Value}".Trim();
            var id = $"what-{Slugify(subject)}";

            // Get surrounding sentence as context
            var sentenceStart = text.LastIndexOf('.', match.Index, Math.Min(match.Index, 200));
            var sentenceEnd = text.IndexOf('.', match.Index + match.Length);
            if (sentenceStart < 0) sentenceStart = Math.Max(0, match.Index - 100);
            if (sentenceEnd < 0) sentenceEnd = Math.Min(text.Length, match.Index + match.Length + 100);

            var sentence = text[(sentenceStart + 1)..sentenceEnd].Trim();
            if (sentence.Length < 10) continue;

            entries.Add(new WikiEntry
            {
                Id = id,
                Dimension = WikiDimension.What,
                Subject = subject,
                Facts = [CreateFact(sentence, sourceId)]
            });
        }

        return entries;
    }

    private static List<WikiEntry> ExtractWhenFacts(string text, string sourceId)
    {
        var entries = new List<WikiEntry>();

        // ISO date patterns, common date formats
        var pattern = @"\b(\d{4}-\d{2}-\d{2}|\w+day(?:\s+\w+\s+\d{1,2})?|(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)\w*\s+\d{1,2}(?:,?\s*\d{4})?)\b";
        foreach (System.Text.RegularExpressions.Match match in
            System.Text.RegularExpressions.Regex.Matches(text, pattern))
        {
            var dateStr = match.Groups[1].Value.Trim();
            if (dateStr.Length < 4) continue;

            // Get surrounding context
            var start = Math.Max(0, match.Index - 80);
            var end = Math.Min(text.Length, match.Index + match.Length + 80);
            var context = text[start..end].Trim();

            var id = $"when-{Slugify(dateStr)}";
            entries.Add(new WikiEntry
            {
                Id = id,
                Dimension = WikiDimension.When,
                Subject = dateStr,
                Facts = [CreateFact(context, sourceId)]
            });
        }

        return entries;
    }

    private static WikiFact CreateFact(string claim, string sourceId) => new()
    {
        Claim = claim,
        Confidence = 0.7, // Heuristic extraction gets moderate confidence
        Source = sourceId,
        LastConfirmed = DateTimeOffset.UtcNow,
        EstimatedTokens = (int)Math.Ceiling(claim.Length / 4.0)
    };

    private static string Slugify(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            text.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-").Trim('-');

    private static bool IsCommonWord(string word) =>
        word is "The" or "This" or "That" or "These" or "Those" or "There" or "Here"
            or "What" or "Where" or "When" or "How" or "Why" or "Who";
}

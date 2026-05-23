using System.Text.RegularExpressions;
using LeanKernel.Abstractions.Interfaces;

namespace LeanKernel.Agents.Enhancement;

/// <summary>
/// Injects inline citations when retrieved knowledge clearly supports a response sentence.
/// </summary>
public sealed partial class CitationInjectionStep : IEnhancementStep
{
    /// <inheritdoc />
    public string Name => "citation-injection";

    /// <inheritdoc />
    public int Order => 30;

    /// <inheritdoc />
    public Task<EnhancementStepOutput> ExecuteAsync(EnhancementStepInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.RetrievedKnowledge is null || input.RetrievedKnowledge.Count == 0)
        {
            return Task.FromResult(CreateNoChange(input.Response, "No retrieved knowledge was available."));
        }

        var matches = SentenceRegex().Matches(input.Response);
        if (matches.Count == 0)
        {
            return Task.FromResult(CreateNoChange(input.Response, "No sentence segments were detected."));
        }

        var builder = new System.Text.StringBuilder(input.Response.Length + 64);
        var modified = false;

        foreach (Match match in matches)
        {
            var segment = match.Value;
            if (segment.Contains("[source:", StringComparison.OrdinalIgnoreCase)
                || segment.Contains("Sources:", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(segment);
                continue;
            }

            var candidate = input.RetrievedKnowledge.FirstOrDefault(item => EnhancementTextMatcher.IsRelevant(segment, item));
            if (candidate is null)
            {
                builder.Append(segment);
                continue;
            }

            builder.Append(InsertCitation(segment, EnhancementTextMatcher.ResolveCitationKey(candidate)));
            modified = true;
        }

        if (!modified)
        {
            return Task.FromResult(CreateNoChange(input.Response, "No sentence matched retrieved knowledge strongly enough."));
        }

        return Task.FromResult(new EnhancementStepOutput
        {
            Response = builder.ToString(),
            Modified = true,
            Reason = "Injected inline source markers for matched content."
        });
    }

    private static string InsertCitation(string segment, string sourceKey)
    {
        var trailingWhitespaceLength = segment.Length - segment.TrimEnd().Length;
        var trailingWhitespace = trailingWhitespaceLength > 0 ? segment[^trailingWhitespaceLength..] : string.Empty;
        var trimmedSegment = segment.TrimEnd();
        var punctuation = trimmedSegment.Length > 0 && char.IsPunctuation(trimmedSegment[^1])
            ? trimmedSegment[^1].ToString()
            : string.Empty;
        var body = punctuation.Length > 0 ? trimmedSegment[..^1] : trimmedSegment;
        return $"{body} [source: {sourceKey}]{punctuation}{trailingWhitespace}";
    }

    private static EnhancementStepOutput CreateNoChange(string response, string reason)
        => new()
        {
            Response = response,
            Modified = false,
            Reason = reason
        };

    [GeneratedRegex(@"[^.!?]+(?:[.!?]+|$)\s*", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceRegex();
}

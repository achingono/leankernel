using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Agents.Enhancement;

/// <summary>
/// Appends a compact source summary when retrieved knowledge clearly informed the response.
/// </summary>
public sealed class KnowledgeSynthesisStep : IEnhancementStep
{
    /// <inheritdoc />
    public string Name => "knowledge-synthesis";

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public Task<EnhancementStepOutput> ExecuteAsync(EnhancementStepInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.RetrievedKnowledge is null || input.RetrievedKnowledge.Count == 0)
        {
            return Task.FromResult(CreateNoChange(input.Response, "No retrieved knowledge was available."));
        }

        if (input.Response.Contains("Sources:", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(CreateNoChange(input.Response, "Sources note already present."));
        }

        var relevantCandidates = EnhancementTextMatcher.FindRelevantCandidates(input.Response, input.RetrievedKnowledge);
        if (relevantCandidates.Count == 0)
        {
            return Task.FromResult(CreateNoChange(input.Response, "No relevant knowledge overlap was detected."));
        }

        var sourceKeys = relevantCandidates
            .Select(EnhancementTextMatcher.ResolveCitationKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourcesNote = $"Sources: {string.Join(", ", sourceKeys)}";
        var enhancedResponse = $"{input.Response.TrimEnd()}\n\n{sourcesNote}";

        return Task.FromResult(new EnhancementStepOutput
        {
            Response = enhancedResponse,
            Modified = true,
            Reason = $"Appended {sourceKeys.Length} supporting source reference(s)."
        });
    }

    private static EnhancementStepOutput CreateNoChange(string response, string reason)
        => new()
        {
            Response = response,
            Modified = false,
            Reason = reason
        };
}

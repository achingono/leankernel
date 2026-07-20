using System.Diagnostics.Metrics;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace LeanKernel.Logic.Memory;

/// <summary>
/// Refines deterministic memory links with optional LLM-assisted graph reasoning.
/// </summary>
public sealed class MemoryGraphReasoner
{
    private static readonly Meter Meter = new("LeanKernel.Logic.Memory", "1.0.0");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("memory.graph.duration.ms");

    private readonly IReasoningModel _reasoningModel;
    private readonly ILogger<MemoryGraphReasoner> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryGraphReasoner"/> class.
    /// </summary>
    /// <param name="reasoningModel">The reasoning model used for graph refinement.</param>
    /// <param name="logger">The logger used for JSON parsing warnings.</param>
    public MemoryGraphReasoner(IReasoningModel reasoningModel, ILogger<MemoryGraphReasoner> logger)
    {
        _reasoningModel = reasoningModel;
        _logger = logger;
    }

    /// <summary>
    /// Refines deterministic links with high-confidence model-inferred edges.
    /// </summary>
    /// <param name="target">The target page being linked.</param>
    /// <param name="fields">The parsed 5W1H fields for the target page.</param>
    /// <param name="deterministicLinks">The deterministic links produced before reasoning.</param>
    /// <param name="candidatePages">The candidate pages available for linking.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The refined set of links.</returns>
    public async Task<IReadOnlyList<MemoryPageLink>> RefineLinksAsync(
        MemoryPageSnapshot target,
        IReadOnlyDictionary<string, string?> fields,
        IReadOnlyList<MemoryPageLink> deterministicLinks,
        IReadOnlyList<MemoryPageSnapshot> candidatePages,
        CancellationToken cancellationToken)
    {
        if (!_reasoningModel.Enabled || deterministicLinks.Count == 0)
        {
            return deterministicLinks;
        }

        var started = DateTimeOffset.UtcNow;
        try
        {
            var request = new GraphReasoningRequest(
                target.FactText,
                fields,
                deterministicLinks,
                candidatePages.Take(12).Select(ToEvidence).ToList());

            var text = await _reasoningModel.CompleteAsync(
                "Propose bounded memory graph edges. Return strict JSON.",
                JsonSerializer.Serialize(request, ModelResponseJson.Options),
                512,
                cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text) || !TryExtractJsonObject(text, out var json))
            {
                return deterministicLinks;
            }

            var parsed = JsonSerializer.Deserialize<GraphReasoningResponse>(json, ModelResponseJson.Options);
            if (parsed?.Links is null || parsed.Links.Count == 0)
            {
                return deterministicLinks;
            }

            var candidateSet = candidatePages.Select(static p => p.Key).ToHashSet(StringComparer.Ordinal);
            var llmLinks = parsed.Links
                .Where(edge => edge.Confidence >= 0.7)
                .Where(edge => candidateSet.Contains(edge.TargetKey))
                .Take(3)
                .Select(edge => new MemoryPageLink(
                    edge.TargetKey,
                    "llm-inferred",
                    (int)Math.Round(edge.Confidence * 100, MidpointRounding.AwayFromZero),
                    edge.Reasons,
                    edge.Confidence,
                    "llm"))
                .ToList();

            return deterministicLinks
                .Concat(llmLinks)
                .GroupBy(static x => x.TargetKey, StringComparer.Ordinal)
                .Select(static g => g.OrderByDescending(x => x.Score).First())
                .OrderByDescending(static x => x.Score)
                .ThenBy(static x => x.TargetKey, StringComparer.Ordinal)
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Graph reasoning JSON parse failed.");
            return deterministicLinks;
        }
        finally
        {
            Duration.Record((DateTimeOffset.UtcNow - started).TotalMilliseconds);
        }
    }

    /// <summary>
    /// Converts a page snapshot into a compact related evidence payload.
    /// </summary>
    /// <param name="snapshot">The page snapshot to convert.</param>
    /// <returns>The related evidence payload.</returns>
    private static RelatedEvidencePage ToEvidence(MemoryPageSnapshot snapshot)
    {
        var snippet = snapshot.FactText.Length <= 320 ? snapshot.FactText : snapshot.FactText[..320];
        return new RelatedEvidencePage(snapshot.Key, snippet, ["candidate"], 0, 0);
    }

    /// <summary>
    /// Tries to extract the outermost JSON object from model output.
    /// </summary>
    /// <param name="content">The model output to inspect.</param>
    /// <param name="json">When this method returns, contains the extracted JSON object.</param>
    /// <returns><c>true</c> when a JSON object was extracted; otherwise, <c>false</c>.</returns>
    private static bool TryExtractJsonObject(string content, out string json)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            json = content[start..(end + 1)];
            return true;
        }

        json = string.Empty;
        return false;
    }
}
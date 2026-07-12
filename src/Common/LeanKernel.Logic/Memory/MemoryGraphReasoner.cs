using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Logic.Memory;

public sealed class MemoryGraphReasoner
{
    private static readonly Meter Meter = new("LeanKernel.Logic.Memory", "1.0.0");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("memory.graph.duration.ms");

    private readonly IReasoningModel _reasoningModel;
    private readonly ILogger<MemoryGraphReasoner> _logger;

    public MemoryGraphReasoner(IReasoningModel reasoningModel, ILogger<MemoryGraphReasoner> logger)
    {
        _reasoningModel = reasoningModel;
        _logger = logger;
    }

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
                JsonSerializer.Serialize(request, SmallModelJson.Options),
                512,
                cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text) || !TryExtractJsonObject(text, out var json))
            {
                return deterministicLinks;
            }

            var parsed = JsonSerializer.Deserialize<GraphReasoningResponse>(json, SmallModelJson.Options);
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

    private static RelatedEvidencePage ToEvidence(MemoryPageSnapshot snapshot)
    {
        var snippet = snapshot.FactText.Length <= 320 ? snapshot.FactText : snapshot.FactText[..320];
        return new RelatedEvidencePage(snapshot.Key, snippet, ["candidate"], 0, 0);
    }

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

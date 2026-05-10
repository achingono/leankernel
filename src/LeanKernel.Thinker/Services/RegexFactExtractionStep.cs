using Microsoft.Extensions.Logging;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Services;

/// <summary>
/// Extracts deterministic wiki facts from a completed turn.
/// </summary>
public sealed class RegexFactExtractionStep : ILearningStep
{
    private readonly IWikiStore _wiki;
    private readonly ILogger<RegexFactExtractionStep> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RegexFactExtractionStep" /> class.
    /// </summary>
    /// <param name="wiki">The wiki store that receives extracted facts.</param>
    /// <param name="logger">The logger used for extraction diagnostics.</param>
    public RegexFactExtractionStep(IWikiStore wiki, ILogger<RegexFactExtractionStep> logger)
    {
        _wiki = wiki;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "regex-fact-extraction";

    /// <inheritdoc />
    public async Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct)
    {
        var facts = WikiExtractor.ExtractFacts(
            turnEvent.UserMessage.Content,
            turnEvent.AssistantResponse,
            turnEvent.SourceId);

        if (facts.Count == 0)
            return LearningStepResult.Succeeded(Name, "No facts extracted.");

        await _wiki.IngestFactsAsync(facts, ct);
        _logger.LogDebug("Regex extraction ingested {Count} wiki entries for {TurnEventId}",
            facts.Count, turnEvent.Id);

        return LearningStepResult.Succeeded(Name, $"Ingested {facts.Count} wiki entries.");
    }
}

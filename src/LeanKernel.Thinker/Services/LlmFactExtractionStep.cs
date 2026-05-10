using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Services;

/// <summary>
/// Extracts semantic wiki facts from a completed turn using the configured LLM.
/// </summary>
public sealed class LlmFactExtractionStep : ILearningStep
{
    private readonly LlmWikiExtractor _extractor;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmFactExtractionStep" /> class.
    /// </summary>
    /// <param name="extractor">The semantic wiki extractor used by this step.</param>
    public LlmFactExtractionStep(LlmWikiExtractor extractor)
    {
        _extractor = extractor;
    }

    /// <inheritdoc />
    public string Name => "llm-fact-extraction";

    /// <inheritdoc />
    public async Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct)
    {
        await _extractor.ExtractAndIngestAsync(
            turnEvent.UserMessage.Content,
            turnEvent.AssistantResponse,
            turnEvent.SourceId,
            ct);

        return LearningStepResult.Succeeded(Name);
    }
}

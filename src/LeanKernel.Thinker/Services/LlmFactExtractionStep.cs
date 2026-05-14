using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Services;

/// <summary>
/// Extracts semantic wiki facts from a completed turn using the configured LLM.
/// </summary>
public sealed class LlmFactExtractionStep : ILearningStep
{
    private readonly IWikiFactExtractor _extractor;
    private readonly WikiFactMapper _mapper;
    private readonly IWikiStore _wiki;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmFactExtractionStep" /> class.
    /// </summary>
    /// <param name="extractor">The semantic wiki extractor used by this step.</param>
    /// <param name="mapper">The mapper used to build canonical wiki entries.</param>
    /// <param name="wiki">The wiki store to ingest mapped entries into.</param>
    public LlmFactExtractionStep(IWikiFactExtractor extractor, WikiFactMapper mapper, IWikiStore wiki)
    {
        _extractor = extractor;
        _mapper = mapper;
        _wiki = wiki;
    }

    /// <inheritdoc />
    public string Name => "llm-fact-extraction";

    /// <inheritdoc />
    public async Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct)
    {
        var extracted = await _extractor.ExtractAsync(
            turnEvent.UserMessage.Content,
            turnEvent.AssistantResponse,
            turnEvent.SourceId,
            ct);

        var entries = _mapper.Map(extracted, turnEvent.SourceId);
        if (entries.Count == 0)
            return LearningStepResult.Succeeded(Name, "No facts extracted.");

        await _wiki.IngestFactsAsync(entries, ct);
        return LearningStepResult.Succeeded(Name);
    }
}

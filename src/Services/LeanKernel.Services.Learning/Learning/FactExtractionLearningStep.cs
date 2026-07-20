using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Extracts candidate factual statements from assistant responses.
/// </summary>
/// <param name="coordinator">Persists extracted facts.</param>
public sealed class FactExtractionLearningStep(IKnowledgePageUpdateCoordinator coordinator) : ILearningPipelineStep
{
    /// <inheritdoc />
    public string StepName => "fact-extraction";

    /// <inheritdoc />
    public async Task ExecuteAsync(CompletedTurnEvent turnEvent, CancellationToken cancellationToken = default)
    {
        var candidateText = turnEvent.ResponseMessages
            .Where(static message => string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            .Select(static message => message.Text)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .ToList();

        if (candidateText.Count == 0)
        {
            return;
        }

        var facts = candidateText
            .SelectMany(static text => text.Split(['.', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(static sentence => sentence.Length > 20)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        foreach (var fact in facts)
        {
            await coordinator.WriteFactAsync(turnEvent, fact, cancellationToken).ConfigureAwait(false);
        }
    }
}

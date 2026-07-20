using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Detects assistant responses that indicate unavailable capabilities.
/// </summary>
/// <param name="coordinator">Persists detected capability gaps.</param>
public sealed class CapabilityGapLearningStep(IKnowledgePageUpdateCoordinator coordinator) : ILearningPipelineStep
{
    /// <inheritdoc />
    public string StepName => "capability-gap";

    private static readonly string[] GapPhrases =
    [
        "i can't",
        "i cannot",
        "i don't have access",
        "i am unable",
        "not available"
    ];

    public async Task ExecuteAsync(CompletedTurnEvent turnEvent, CancellationToken cancellationToken = default)
    {
        foreach (var message in turnEvent.ResponseMessages.Where(static message => string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)))
        {
            var lower = message.Text.ToLowerInvariant();
            if (!GapPhrases.Any(lower.Contains))
            {
                continue;
            }

            await coordinator.WriteCapabilityGapAsync(turnEvent, message.Text, cancellationToken).ConfigureAwait(false);
        }
    }
}
    /// <inheritdoc />

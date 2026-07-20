using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Extracts identity intent signals from user-authored messages.
/// </summary>
/// <param name="coordinator">Persists extracted identity intent.</param>
public sealed class IdentityIntentLearningStep(IKnowledgePageUpdateCoordinator coordinator) : ILearningPipelineStep
{
    /// <inheritdoc />
    public string StepName => "identity-intent";

    private static readonly string[] IdentityPhrases =
    [
        "my name is",
        "i am",
        "i'm",
        "i live in",
        "my email is",
        "you can call me"
    ];

    public async Task ExecuteAsync(CompletedTurnEvent turnEvent, CancellationToken cancellationToken = default)
    {
        foreach (var message in turnEvent.RequestMessages.Where(static message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase)))
        {
            var lower = message.Text.ToLowerInvariant();
            if (!IdentityPhrases.Any(lower.Contains))
            {
                continue;
            }

            await coordinator.WriteIdentityIntentAsync(turnEvent, message.Text, cancellationToken).ConfigureAwait(false);
        }
    }
}
    /// <inheritdoc />

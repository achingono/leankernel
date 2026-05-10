using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Services;

/// <summary>
/// Updates identity files from completed turn insights.
/// </summary>
public sealed class IdentityRefreshStep : ILearningStep
{
    private readonly IIdentityFileUpdateService _identityUpdater;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdentityRefreshStep" /> class.
    /// </summary>
    /// <param name="identityUpdater">The identity file updater used by this step.</param>
    public IdentityRefreshStep(IIdentityFileUpdateService identityUpdater)
    {
        _identityUpdater = identityUpdater;
    }

    /// <inheritdoc />
    public string Name => "identity-refresh";

    /// <inheritdoc />
    public async Task<LearningStepResult> ProcessAsync(TurnEvent turnEvent, CancellationToken ct)
    {
        await _identityUpdater.UpdateFromTurnAsync(
            turnEvent.UserMessage.Content,
            turnEvent.AssistantResponse,
            turnEvent.SessionId,
            ct);

        return LearningStepResult.Succeeded(Name);
    }
}

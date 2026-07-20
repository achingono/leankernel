using LeanKernel.Logic.Providers;
using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Persists onboarding directives as scoped memory pages.
/// </summary>
/// <param name="memoryClient">Memory client used for persistence.</param>
public sealed class MemoryOnboardingDirectivePublisher(IMemoryClient memoryClient) : IOnboardingDirectivePublisher
{
    /// <inheritdoc />
    public Task PublishAsync(CompletedTurnEvent turnEvent, string directive, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directive);

        var scope = new MemoryScope
        {
            TenantId = turnEvent.TenantId,
            PersonId = turnEvent.PersonId,
            ChannelId = turnEvent.ChannelId
        };

        var key = $"onboarding/directives/{turnEvent.TurnId}/{Guid.NewGuid():N}";
        var content = $"# Onboarding Directive\n\n{directive}\n\n- Session: {turnEvent.SessionId ?? "unknown"}\n- Turn: {turnEvent.TurnId}\n- RecordedAt: {turnEvent.RecordedAt:O}\n";
        return memoryClient.SaveMemoryAsync(scope, key, content, cancellationToken);
    }
}

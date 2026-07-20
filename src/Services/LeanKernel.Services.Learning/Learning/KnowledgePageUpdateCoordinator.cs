using System.Text;

using LeanKernel.Logic.Providers;
using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Learning.Learning;

/// <summary>
/// Writes learning artifacts to scoped memory pages.
/// </summary>
/// <param name="memoryClient">Memory client used to persist artifacts.</param>
public sealed class KnowledgePageUpdateCoordinator(IMemoryClient memoryClient) : IKnowledgePageUpdateCoordinator
{
    /// <inheritdoc />
    public Task WriteFactAsync(CompletedTurnEvent turnEvent, string fact, CancellationToken cancellationToken = default)
    {
        return SaveAsync(turnEvent, "facts/what/learned", "Learned Fact", fact, cancellationToken);
    }

    /// <inheritdoc />
    public Task WriteIdentityIntentAsync(CompletedTurnEvent turnEvent, string intent, CancellationToken cancellationToken = default)
    {
        return SaveAsync(turnEvent, "identity/intent", "Identity Intent", intent, cancellationToken);
    }

    /// <inheritdoc />
    public Task WriteCapabilityGapAsync(CompletedTurnEvent turnEvent, string gap, CancellationToken cancellationToken = default)
    {
        return SaveAsync(turnEvent, "capability/gap", "Capability Gap", gap, cancellationToken);
    }

    /// <inheritdoc />
    public Task WriteEngagementSignalAsync(CompletedTurnEvent turnEvent, string signal, CancellationToken cancellationToken = default)
    {
        return SaveAsync(turnEvent, "engagement/signal", "Engagement Signal", signal, cancellationToken);
    }

    private Task SaveAsync(
        CompletedTurnEvent turnEvent,
        string keyPrefix,
        string title,
        string value,
        CancellationToken cancellationToken)
    {
        var scope = new MemoryScope
        {
            TenantId = turnEvent.TenantId,
            PersonId = turnEvent.PersonId,
            ChannelId = turnEvent.ChannelId
        };

        var safeTitle = title.Trim();
        var safeValue = value.Trim();
        var key = $"{keyPrefix}/{turnEvent.TurnId}/{Guid.NewGuid():N}";

        var contentBuilder = new StringBuilder();
        contentBuilder.AppendLine($"# {safeTitle}");
        contentBuilder.AppendLine();
        contentBuilder.AppendLine(safeValue);
        contentBuilder.AppendLine();
        contentBuilder.AppendLine($"- Session: {turnEvent.SessionId ?? "unknown"}");
        contentBuilder.AppendLine($"- Turn: {turnEvent.TurnId}");
        contentBuilder.AppendLine($"- RecordedAt: {turnEvent.RecordedAt:O}");

        return memoryClient.SaveMemoryAsync(scope, key, contentBuilder.ToString(), cancellationToken);
    }
}

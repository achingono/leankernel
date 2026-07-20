namespace LeanKernel.Services.Learning.Learning;

using LeanKernel.Services.Common.Contracts;

/// <summary>
/// Persists learning artifacts derived from completed turns.
/// </summary>
public interface IKnowledgePageUpdateCoordinator
{
    /// <summary>
    /// Writes a learned fact extracted from a completed turn.
    /// </summary>
    Task WriteFactAsync(CompletedTurnEvent turnEvent, string fact, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes learned identity intent extracted from a completed turn.
    /// </summary>
    Task WriteIdentityIntentAsync(CompletedTurnEvent turnEvent, string intent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a capability gap discovered in assistant responses.
    /// </summary>
    Task WriteCapabilityGapAsync(CompletedTurnEvent turnEvent, string gap, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes an engagement signal computed for a completed turn.
    /// </summary>
    Task WriteEngagementSignalAsync(CompletedTurnEvent turnEvent, string signal, CancellationToken cancellationToken = default);
}

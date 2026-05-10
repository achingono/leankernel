namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Service for updating identity files (USER.md, SELF.md) from conversation insights.
/// Enables continuous agent self-improvement without explicit prompting.
/// </summary>
public interface IIdentityFileUpdateService
{
    /// <summary>
    /// Extract insights from a conversation turn and update identity files.
    /// </summary>
    Task UpdateFromTurnAsync(
        string userMessage,
        string assistantResponse,
        string sessionId,
        CancellationToken ct);
}

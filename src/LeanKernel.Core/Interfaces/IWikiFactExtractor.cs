using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Extracts structured wiki fact DTOs from a conversation exchange.
/// </summary>
public interface IWikiFactExtractor
{
    /// <summary>
    /// Extract structured facts from a user/assistant exchange.
    /// </summary>
    Task<IReadOnlyList<ExtractedWikiFact>> ExtractAsync(
        string userMessage,
        string assistantResponse,
        string sourceId,
        CancellationToken ct);
}

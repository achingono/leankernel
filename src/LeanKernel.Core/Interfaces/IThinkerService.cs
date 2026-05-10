using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// The Thinker's main contract — takes a message, produces a response
/// using gated context from the Archivist.
/// </summary>
public interface IThinkerService
{
    /// <summary>
    /// Processes an inbound message and returns the assistant response.
    /// </summary>
    /// <param name="message">The inbound message to process.</param>
    /// <param name="ct">A token used to cancel processing.</param>
    /// <returns>The assistant response text.</returns>
    Task<string> ProcessAsync(LeanKernelMessage message, CancellationToken ct);
}

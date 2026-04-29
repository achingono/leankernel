using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// The Thinker's main contract — takes a message, produces a response
/// using gated context from the Archivist.
/// </summary>
public interface IThinkerService
{
    Task<string> ProcessAsync(LeanKernelMessage message, CancellationToken ct);
}

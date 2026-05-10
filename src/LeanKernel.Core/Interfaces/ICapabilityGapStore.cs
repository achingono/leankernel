using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Persists capability gaps and exposes them for future prompt context.
/// </summary>
public interface ICapabilityGapStore
{
    /// <summary>
    /// Appends an observed capability gap to durable storage.
    /// </summary>
    /// <param name="gap">The capability gap to persist.</param>
    /// <param name="ct">A token used to cancel persistence.</param>
    Task AppendAsync(CapabilityGap gap, CancellationToken ct);

    /// <summary>
    /// Reads a concise prompt section describing recently observed capability gaps.
    /// </summary>
    /// <param name="ct">A token used to cancel reading.</param>
    /// <returns>A prompt-ready markdown section, or <see langword="null" /> when there are no gaps.</returns>
    Task<string?> ReadPromptSectionAsync(CancellationToken ct);
}

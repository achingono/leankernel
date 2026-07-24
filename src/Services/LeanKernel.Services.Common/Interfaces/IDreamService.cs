namespace LeanKernel.Services.Common.Interfaces;

/// <summary>
/// Abstraction for invoking GBrain Dream cycles.
/// Implementations live in Gateway; consumed by Learning.
/// </summary>
public interface IDreamService
{
    /// <summary>
    /// Runs a Dream cycle for the given source scope and mode.
    /// </summary>
    /// <param name="sourceScope">The source scope for the Dream cycle.</param>
    /// <param name="mode">The mode for the Dream cycle.</param>
    /// <param name="ct">The cancellation token for the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task<DreamRunResult> RunDreamAsync(
        string sourceScope,
        string mode,
        CancellationToken ct = default);
}
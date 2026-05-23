using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Runs the configured synchronous response enhancement pipeline.
/// </summary>
public interface IResponseEnhancer
{
    /// <summary>
    /// Enhances a response before it is delivered to the user.
    /// </summary>
    /// <param name="input">The enhancement input.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The structured enhancement result.</returns>
    Task<EnhancementResult> EnhanceAsync(EnhancementStepInput input, CancellationToken ct = default);
}

namespace LeanKernel.Logic.Diagnostics;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Provides methods for evaluating the health of the system and its components.
/// </summary>
public interface IHealthAggregator
{
    /// <summary>
    /// Checks whether the overall system is healthy.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns><c>true</c> if the system is healthy; otherwise, <c>false</c>.</returns>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the LiteLLM component is healthy.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns><c>true</c> if LiteLLM is healthy; otherwise, <c>false</c>.</returns>
    Task<bool> IsLiteLlmHealthyAsync(CancellationToken cancellationToken = default);
}
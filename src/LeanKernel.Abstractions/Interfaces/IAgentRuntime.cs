using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Defines the core execution interface for an agent runtime.
/// </summary>
public interface IAgentRuntime
{
    /// <summary>
    /// Executes a single turn in the agent runtime.
    /// </summary>
    /// <param name="message">The message to process.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous execution, containing the processed response.</returns>
    Task<string> RunTurnAsync(LeanKernelMessage message, CancellationToken ct = default);

    /// <summary>
    /// Executes a single turn in the agent runtime and returns structured response data.
    /// </summary>
    /// <param name="message">The message to process.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous execution, containing response content and optional attachments.</returns>
    Task<AgentResponse> RunTurnDetailedAsync(LeanKernelMessage message, CancellationToken ct = default);
}

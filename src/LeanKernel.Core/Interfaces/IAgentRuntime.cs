using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Provides the canonical entry point for running a single agent turn.
/// </summary>
public interface IAgentRuntime
{
    /// <summary>
    /// Runs the agent for one inbound message and returns the response that should be sent to the caller.
    /// </summary>
    /// <param name="message">The inbound message envelope for the turn.</param>
    /// <param name="ct">A token used to cancel the turn.</param>
    /// <returns>The assistant response text for the completed turn.</returns>
    Task<string> RunTurnAsync(LeanKernelMessage message, CancellationToken ct);
}

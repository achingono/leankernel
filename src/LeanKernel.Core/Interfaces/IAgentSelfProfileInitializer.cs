using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Initializes and validates the persisted self-profile for an agent.
/// </summary>
public interface IAgentSelfProfileInitializer
{
    /// <summary>
    /// Ensures the agent self-profile exists before profile synchronization runs.
    /// </summary>
    /// <param name="ct">A token used to cancel the initialization operation.</param>
    /// <returns>The result describing whether the self-profile was initialized successfully.</returns>
    Task<ConfigurationStepResult> InitializeAsync(CancellationToken ct = default);
}

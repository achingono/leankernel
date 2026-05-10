using LeanKernel.Core.Configuration;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Stores the mutable runtime LeanKernel configuration.
/// </summary>
public interface IRuntimeLeanKernelConfigStore
{
    /// <summary>
    /// Gets the current runtime configuration snapshot.
    /// </summary>
    /// <returns>The current runtime configuration.</returns>
    LeanKernelConfig GetCurrent();

    /// <summary>
    /// Persists a runtime configuration snapshot.
    /// </summary>
    /// <param name="config">The configuration to persist.</param>
    /// <param name="ct">A token used to cancel the write.</param>
    Task SaveAsync(LeanKernelConfig config, CancellationToken ct);
}

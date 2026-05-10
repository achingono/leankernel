using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Initializes and synchronizes the persisted user profile from learned facts.
/// </summary>
public interface IUserProfileSynchronizer
{
    /// <summary>
    /// Ensures the user profile exists before synchronization runs.
    /// </summary>
    /// <param name="ct">A token used to cancel the initialization operation.</param>
    /// <returns>The result describing whether the user profile was initialized successfully.</returns>
    Task<ConfigurationStepResult> InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Synchronizes the user profile from accumulated wiki facts.
    /// </summary>
    /// <param name="ct">A token used to cancel the synchronization operation.</param>
    /// <returns>The result describing whether synchronization completed successfully.</returns>
    Task<ConfigurationStepResult> SyncFromWikiAsync(CancellationToken ct = default);
}

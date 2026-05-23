using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Loads durable identity information used to personalize prompt construction.
/// </summary>
public interface IIdentityProvider
{
    /// <summary>
    /// Loads identity context for the specified user.
    /// </summary>
    /// <param name="userId">The user identifier for the active turn.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The resolved identity context.</returns>
    Task<IdentityContext> LoadIdentityAsync(string userId, CancellationToken ct = default);
}

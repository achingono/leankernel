using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Authorizes semantic action types against the current engagement policy.
/// </summary>
public interface IActionAuthorizer
{
    /// <summary>
    /// Checks whether the specified action type is authorized.
    /// </summary>
    /// <param name="actionType">The semantic action type, such as <c>ReadFile</c> or <c>WriteUserMd</c>.</param>
    /// <param name="ct">A token used to cancel the authorization check.</param>
    /// <returns>The authorization decision for the action.</returns>
    Task<AuthorizationResult> AuthorizeAsync(string actionType, CancellationToken ct);
}

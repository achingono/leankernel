using System.Security.Principal;

namespace LeanKernel.Services.Gateway.Requests;

/// <summary>
/// Provides access to the current request principal.
/// </summary>
public interface IPrincipalAccessor
{
    /// <summary>
    /// Gets the current request principal, if one is available.
    /// </summary>
    IPrincipal? Principal { get; }
}
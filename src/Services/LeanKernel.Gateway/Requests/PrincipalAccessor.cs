using System.Security.Principal;

namespace LeanKernel.Gateway.Requests;

/// <summary>
/// Provides access to the current request principal from the HTTP context.
/// </summary>
public class PrincipalAccessor : IPrincipalAccessor
{
    private readonly IHttpContextAccessor httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrincipalAccessor"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">Provides access to the current HTTP context.</param>
    public PrincipalAccessor(IHttpContextAccessor httpContextAccessor)
    {
        this.httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public IPrincipal? Principal => httpContextAccessor?.HttpContext?.User;

}
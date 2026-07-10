using System.Security.Principal;

namespace LeanKernel.Requests;

public class PrincipalAccessor : IPrincipalAccessor
{
    private readonly IHttpContextAccessor httpContextAccessor;

    public PrincipalAccessor(IHttpContextAccessor httpContextAccessor)
    {
        this.httpContextAccessor = httpContextAccessor;
    }

    public IPrincipal? Principal => httpContextAccessor?.HttpContext?.User;

}

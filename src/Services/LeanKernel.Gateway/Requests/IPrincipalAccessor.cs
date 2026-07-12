using System.Security.Principal;

namespace LeanKernel.Gateway.Requests;

public interface IPrincipalAccessor
{
    IPrincipal? Principal { get; }
}

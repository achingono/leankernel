using System.Security.Principal;

namespace LeanKernel.Requests;

public interface IPrincipalAccessor
{
    IPrincipal? Principal { get; }
}

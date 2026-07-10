using System.Security.Principal;

namespace LeanKernel.Requests;

public interface IHostNameAccessor
{
    /// <summary>
    /// Gets the host name of the current request.
    /// </summary>
    string HostName { get; }
}

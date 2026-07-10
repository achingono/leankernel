using System.Security.Principal;

namespace LeanKernel.Requests;

public class HostNameAccessor : IHostNameAccessor
{
    private readonly IHttpContextAccessor httpContextAccessor;

    public HostNameAccessor(IHttpContextAccessor httpContextAccessor)
    {
        this.httpContextAccessor = httpContextAccessor;
    }

    public string HostName
    {
        get
        {
            var host = httpContextAccessor.HttpContext?.Request?.Host.Host;
            return string.IsNullOrEmpty(host) ? "localhost" : host;
        }
    }
}

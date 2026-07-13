namespace LeanKernel.Gateway.Requests;

/// <summary>
/// Provides access to the normalized host name for the current HTTP request.
/// </summary>
public class HostNameAccessor : IHostNameAccessor
{
    private readonly IHttpContextAccessor httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostNameAccessor"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">Provides access to the current HTTP context.</param>
    public HostNameAccessor(IHttpContextAccessor httpContextAccessor)
    {
        this.httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public string HostName
    {
        get
        {
            var host = httpContextAccessor.HttpContext?.Request?.Host.Host;
            return string.IsNullOrEmpty(host) ? "localhost" : host;
        }
    }
}

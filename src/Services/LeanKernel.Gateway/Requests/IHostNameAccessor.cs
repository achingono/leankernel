namespace LeanKernel.Gateway.Requests;

/// <summary>
/// Provides access to the normalized host name for the current request.
/// </summary>
public interface IHostNameAccessor
{
    /// <summary>
    /// Gets the host name of the current request.
    /// </summary>
    string HostName { get; }
}

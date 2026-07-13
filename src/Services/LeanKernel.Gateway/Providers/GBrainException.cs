namespace LeanKernel.Gateway.Providers;

/// <summary>
/// Represents an error returned by the GBrain MCP service.
/// </summary>
public sealed class GBrainException : Exception
{
    /// <summary>
    /// Gets the error code from the GBrain MCP response.
    /// </summary>
    public int ErrorCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GBrainException"/> class.
    /// </summary>
    /// <param name="message">The error message returned by GBrain.</param>
    /// <param name="errorCode">The MCP error code, if one was supplied.</param>
    public GBrainException(string message, int errorCode = 0)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}

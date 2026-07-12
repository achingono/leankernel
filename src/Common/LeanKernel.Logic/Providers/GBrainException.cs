namespace LeanKernel.Logic.Providers;

/// <summary>
/// Represents an error returned by the GBrain MCP service.
/// </summary>
public sealed class GBrainException : Exception
{
    /// <summary>
    /// Gets the error code from the GBrain MCP response.
    /// </summary>
    public int ErrorCode { get; }

    public GBrainException(string message, int errorCode = 0)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}

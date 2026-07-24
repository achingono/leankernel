namespace LeanKernel.Services.Common.Interfaces;

/// <summary>
/// Represents an error returned by the GBrain MCP service.
/// </summary>
#pragma warning disable S3925
public sealed class GBrainException : Exception
#pragma warning restore S3925
{
    /// <summary>
    /// Gets the error code from the GBrain MCP response.
    /// </summary>
    public int ErrorCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GBrainException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The error code from the GBrain MCP response.</param>
    public GBrainException(string message, int errorCode = 0)
        : base(message)
    {
        this.ErrorCode = errorCode;
    }
}
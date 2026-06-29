namespace LeanKernel.Knowledge;

/// <summary>
/// Provides functionality for gbrain exception.
/// </summary>
public sealed class GBrainException : Exception
{
    /// <summary>
    /// Gets error code.
    /// </summary>
    public int ErrorCode { get; }

    public GBrainException(string message, int errorCode = 0)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}

namespace LeanKernel.Knowledge;

public sealed class GBrainException : Exception
{
    public int ErrorCode { get; }

    public GBrainException(string message, int errorCode = 0)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}

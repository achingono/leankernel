using LeanKernel.Abstractions.Interfaces;

namespace LeanKernel.Context;

/// <summary>
/// Simple token estimator using character-based approximation.
/// ~4 characters per token for English text.
/// </summary>
public sealed class SimpleTokenEstimator : ITokenEstimator
{
    private const double CharsPerToken = 4.0;

    public int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return (int)Math.Ceiling(text.Length / CharsPerToken);
    }
}

using LeanKernel.Core.Interfaces;

namespace LeanKernel.Core.Models;

/// <summary>
/// Default token estimator that uses the common four-characters-per-token approximation.
/// </summary>
public sealed class DefaultTokenEstimator : ITokenEstimator
{
    /// <inheritdoc />
    public int EstimateTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return (int)Math.Ceiling(text.Length / 4.0);
    }
}

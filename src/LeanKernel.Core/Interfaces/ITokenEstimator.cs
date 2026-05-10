namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Estimates token counts for model-bound text.
/// </summary>
public interface ITokenEstimator
{
    /// <summary>
    /// Estimates the number of model tokens represented by text.
    /// </summary>
    /// <param name="text">The text to estimate.</param>
    /// <returns>The estimated token count.</returns>
    int EstimateTokens(string? text);
}

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Estimates the number of tokens in a given piece of text.
/// </summary>
public interface ITokenEstimator
{
    /// <summary>
    /// Estimates the tokens in the provided text.
    /// </summary>
    /// <param name="text">The text to estimate.</param>
    /// <returns>The estimated token count.</returns>
    int EstimateTokens(string text);
}

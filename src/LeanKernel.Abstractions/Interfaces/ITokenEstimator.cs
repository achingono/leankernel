namespace LeanKernel.Abstractions.Interfaces;

public interface ITokenEstimator
{
    int EstimateTokens(string text);
}

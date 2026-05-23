using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Evaluates spend-guard decisions for model execution.
/// </summary>
public interface ISpendGuardService
{
    /// <summary>
    /// Estimates request cost in USD.
    /// </summary>
    /// <param name="tier">The model tier.</param>
    /// <param name="inputTokens">The estimated input tokens.</param>
    /// <param name="outputTokens">The estimated output tokens.</param>
    /// <returns>The estimated cost in USD.</returns>
    decimal EstimateCostUsd(ModelTier tier, int inputTokens, int outputTokens);

    /// <summary>
    /// Evaluates whether a request should be allowed, warned, or blocked.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="tier">The model tier.</param>
    /// <param name="estimatedInputTokens">The estimated input tokens.</param>
    /// <param name="estimatedOutputTokens">The estimated output tokens.</param>
    /// <param name="asOf">The optional evaluation time.</param>
    /// <returns>The spend-guard decision.</returns>
    SpendGuardDecision Evaluate(
        string sessionId,
        ModelTier tier,
        int estimatedInputTokens,
        int estimatedOutputTokens,
        DateTimeOffset? asOf = null);
}

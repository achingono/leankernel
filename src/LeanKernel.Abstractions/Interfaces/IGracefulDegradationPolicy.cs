using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Evaluates whether the runtime should degrade behavior for the current provider-health state.
/// </summary>
public interface IGracefulDegradationPolicy
{
    /// <summary>
    /// Evaluates the current runtime degradation decision.
    /// </summary>
    /// <returns>The degradation decision for the current request.</returns>
    GracefulDegradationDecision Evaluate();
}

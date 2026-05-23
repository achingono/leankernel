using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Agents.Resilience;

/// <summary>
/// Produces non-throwing degradation decisions from provider-health state.
/// </summary>
public sealed class GracefulDegradationPolicy(
    IProviderHealthTracker providerHealthTracker,
    ILogger<GracefulDegradationPolicy> logger) : IGracefulDegradationPolicy
{
    private readonly IProviderHealthTracker _providerHealthTracker = providerHealthTracker ?? throw new ArgumentNullException(nameof(providerHealthTracker));
    private readonly ILogger<GracefulDegradationPolicy> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public GracefulDegradationDecision Evaluate()
    {
        var warnings = new List<string>();
        var allowModelExecution = true;
        string? userMessage = null;
        var skipKnowledgeRetrieval = false;
        var persistenceDegraded = false;

        var databaseStatus = _providerHealthTracker.GetStatus(ProviderNames.Database);
        if (!databaseStatus.IsHealthy)
        {
            persistenceDegraded = true;
            warnings.Add("Persistence is temporarily degraded; this turn may not be durably saved.");
        }

        var gbrainStatus = _providerHealthTracker.GetStatus(ProviderNames.GBrain);
        if (!gbrainStatus.IsHealthy)
        {
            skipKnowledgeRetrieval = true;
            warnings.Add("Knowledge retrieval is temporarily degraded; continuing without live GBrain retrieval.");
        }

        var liteLlmStatus = _providerHealthTracker.GetStatus(ProviderNames.LiteLlm);
        if (!liteLlmStatus.IsHealthy)
        {
            allowModelExecution = false;
            userMessage = "LeanKernel cannot reach the configured model provider right now. Please try again shortly.";
        }

        var decision = new GracefulDegradationDecision
        {
            AllowModelExecution = allowModelExecution,
            SkipKnowledgeRetrieval = skipKnowledgeRetrieval,
            PersistenceDegraded = persistenceDegraded,
            UserMessage = userMessage,
            Warnings = warnings,
        };

        if (decision.IsDegraded)
        {
            _logger.LogWarning(
                "Graceful degradation active: allowModelExecution={AllowModelExecution}, skipKnowledgeRetrieval={SkipKnowledgeRetrieval}, persistenceDegraded={PersistenceDegraded}",
                decision.AllowModelExecution,
                decision.SkipKnowledgeRetrieval,
                decision.PersistenceDegraded);
        }

        return decision;
    }
}

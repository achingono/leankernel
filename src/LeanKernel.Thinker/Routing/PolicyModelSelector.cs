using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;

namespace LeanKernel.Thinker.Routing;

/// <summary>
/// Builds the ordered escalation chain for a request based on complexity and
/// current provider health / spend guard state (FR-2, FR-3, FR-5, FR-8).
/// </summary>
public sealed class PolicyModelSelector
{
    private readonly RoutingConfig _config;
    private readonly ProviderHealthTracker _health;
    private readonly SpendGuard _spendGuard;

    /// <summary>
    /// Represents the policy model selector.
    /// </summary>
    public PolicyModelSelector(
        IOptions<LeanKernelConfig> config,
        ProviderHealthTracker health,
        SpendGuard spendGuard)
    {
        _config = config.Value.Routing;
        _health = health;
        _spendGuard = spendGuard;
    }

    /// <summary>
    /// Returns the ordered list of candidates to try for the request.
    /// Free-first (FR-3); paid candidate is appended unless the hard spend limit is active.
    /// </summary>
    public IReadOnlyList<RouteCandidate> BuildCandidates(TaskComplexity complexity)
    {
        var candidates = new List<RouteCandidate>();

        // Primary tier (same as complexity).
        var primary = TierAlias(complexity);
        if (!_health.IsOnCooldown(primary))
            candidates.Add(new RouteCandidate { Tier = primary, Alias = primary, IsPaid = false });

        // Adjacent free tiers (FR-3 §2): tier above (more capable) is preferred as escalation.
        foreach (var adjacentComplexity in AdjacentTiers(complexity))
        {
            var alias = TierAlias(adjacentComplexity);
            if (!_health.IsOnCooldown(alias))
                candidates.Add(new RouteCandidate { Tier = alias, Alias = alias, IsPaid = false });
        }

        // Paid fallback (FR-3 §3): only when hard limit is not active.
        if (!_spendGuard.ILeanKernelLimitActive())
        {
            // The "large" route includes GitHub Copilot as final fallback.
            // We add it here as an explicit paid candidate with a distinct tier label.
            candidates.Add(new RouteCandidate
            {
                Tier = "paid",
                Alias = _config.LargeAlias,
                IsPaid = true
            });
        }

        return candidates;
    }

    private string TierAlias(TaskComplexity complexity) => complexity switch
    {
        TaskComplexity.Small => _config.SmallAlias,
        TaskComplexity.Medium => _config.MediumAlias,
        TaskComplexity.Large => _config.LargeAlias,
        _ => _config.LargeAlias
    };

    private static IEnumerable<TaskComplexity> AdjacentTiers(TaskComplexity complexity)
    {
        // Always escalate upward when free tiers fail (more capable model).
        return complexity switch
        {
            TaskComplexity.Small => [TaskComplexity.Medium, TaskComplexity.Large],
            TaskComplexity.Medium => [TaskComplexity.Large],
            TaskComplexity.Large => [],
            _ => []
        };
    }
}

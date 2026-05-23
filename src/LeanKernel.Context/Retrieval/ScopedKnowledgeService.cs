using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Context.Retrieval;

/// <summary>
/// Applies scope policies, entity-aware expansion, and diagnostics to knowledge retrieval.
/// </summary>
public sealed class ScopedKnowledgeService(
    IKnowledgeService knowledge,
    RetrievalScopePolicy scopePolicy,
    EntityExpander entityExpander,
    IOptions<RetrievalConfig> config,
    ILogger<ScopedKnowledgeService> logger) : IScopedKnowledgeService
{
    private readonly IKnowledgeService _knowledge = knowledge ?? throw new ArgumentNullException(nameof(knowledge));
    private readonly RetrievalScopePolicy _scopePolicy = scopePolicy ?? throw new ArgumentNullException(nameof(scopePolicy));
    private readonly EntityExpander _entityExpander = entityExpander ?? throw new ArgumentNullException(nameof(entityExpander));
    private readonly RetrievalConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
    private readonly ILogger<ScopedKnowledgeService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<ScopedRetrievalResult> RetrieveWithScopeAsync(
        string query,
        string scope,
        int maxResults = 10,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var requestedScope = string.IsNullOrWhiteSpace(scope)
            ? _config.DefaultScope
            : scope.Trim();
        var policy = _scopePolicy.ResolvePolicy(requestedScope);
        var baseCandidates = await _knowledge.SearchAsync(query, maxResults, ct).ConfigureAwait(false);
        var expansion = await _entityExpander.ExpandAsync(query, baseCandidates, ct).ConfigureAwait(false);
        var allCandidates = MergeCandidates(baseCandidates, expansion.ExpandedCandidates);
        var minimumScore = Math.Max(_config.MinScopeRelevanceScore, policy?.MinScore ?? 0.0);
        var decisions = new List<RetrievalCandidateDecision>(allCandidates.Count);
        var admittedCandidates = new List<RetrievalCandidate>(allCandidates.Count);

        foreach (var candidate in allCandidates)
        {
            var adjustedScore = ApplyEntityBoost(candidate, expansion);
            var exclusionReason = DetermineExclusionReason(candidate, adjustedScore, minimumScore, policy);
            var admitted = exclusionReason is null;

            if (admitted)
            {
                admittedCandidates.Add(candidate with { Score = adjustedScore });
            }

            decisions.Add(new RetrievalCandidateDecision
            {
                Key = candidate.Key,
                Source = candidate.Source,
                OriginalScore = candidate.Score,
                AdjustedScore = adjustedScore,
                Admitted = admitted,
                ExclusionReason = exclusionReason
            });
        }

        var sortedAdmitted = SortCandidates(admittedCandidates);
        var effectiveScope = policy?.Name ?? NormalizeScopeName(requestedScope);
        var diagnostics = CreateDiagnostics(decisions, effectiveScope, expansion.ExpandedEntities);

        _logger.LogDebug(
            "Scoped retrieval for {Scope} considered {Considered} candidates and admitted {Admitted}",
            effectiveScope,
            diagnostics.TotalConsidered,
            diagnostics.TotalAdmitted);

        return new ScopedRetrievalResult
        {
            Candidates = sortedAdmitted,
            Diagnostics = diagnostics
        };
    }

    private RetrievalDiagnostics CreateDiagnostics(
        IReadOnlyList<RetrievalCandidateDecision> decisions,
        string effectiveScope,
        IReadOnlyList<string> expandedEntities)
    {
        var excludedByScore = decisions.Count(decision => string.Equals(decision.ExclusionReason, "low_score", StringComparison.Ordinal));
        var excludedByScope = decisions.Count(decision => !decision.Admitted && !string.Equals(decision.ExclusionReason, "low_score", StringComparison.Ordinal));

        return new RetrievalDiagnostics
        {
            SessionId = "unknown",
            TurnId = "unknown",
            Decisions = _config.EmitRetrievalDiagnostics ? decisions : [],
            TotalConsidered = decisions.Count,
            TotalAdmitted = decisions.Count(decision => decision.Admitted),
            TotalExcludedByScope = excludedByScope,
            TotalExcludedByScore = excludedByScore,
            EffectiveScope = effectiveScope,
            ExpandedEntities = _config.EmitRetrievalDiagnostics ? expandedEntities : []
        };
    }

    private string? DetermineExclusionReason(
        RetrievalCandidate candidate,
        double adjustedScore,
        double minimumScore,
        ScopePolicyDefinition? policy)
    {
        if (policy is null)
        {
            return "unknown_scope";
        }

        if (!MatchesPolicy(policy, candidate, out var scopeReason))
        {
            return scopeReason;
        }

        return adjustedScore < minimumScore ? "low_score" : null;
    }

    private bool MatchesPolicy(
        ScopePolicyDefinition policy,
        RetrievalCandidate candidate,
        out string exclusionReason)
    {
        var candidateNamespace = ResolveNamespace(candidate);

        if (policy.IncludeNamespaces.Count > 0)
        {
            var included = candidateNamespace is not null && policy.IncludeNamespaces.Contains(candidateNamespace, StringComparer.OrdinalIgnoreCase);
            if (!included)
            {
                exclusionReason = "out_of_scope_namespace";
                return false;
            }
        }

        if (candidateNamespace is not null && policy.ExcludeNamespaces.Contains(candidateNamespace, StringComparer.OrdinalIgnoreCase))
        {
            exclusionReason = "out_of_scope_namespace";
            return false;
        }

        foreach (var requiredKey in policy.RequiredMetadataKeys)
        {
            if (!TryGetMetadataValue(candidate.Metadata, requiredKey, out _))
            {
                exclusionReason = $"missing_metadata:{requiredKey}";
                return false;
            }
        }

        exclusionReason = string.Empty;
        return true;
    }

    private double ApplyEntityBoost(RetrievalCandidate candidate, EntityExpansionResult expansion)
    {
        if (_config.EntityBoostMultiplier <= 1.0)
        {
            return candidate.Score;
        }

        if (!MatchesExpandedEntity(candidate, expansion))
        {
            return candidate.Score;
        }

        return candidate.Score * _config.EntityBoostMultiplier;
    }

    private static bool MatchesExpandedEntity(RetrievalCandidate candidate, EntityExpansionResult expansion)
    {
        if (expansion.BoostedCandidateKeys.Contains(candidate.Key))
        {
            return true;
        }

        foreach (var entity in expansion.ExpandedEntities)
        {
            if (ContainsIgnoreCase(candidate.Key, entity) || ContainsIgnoreCase(candidate.Content, entity))
            {
                return true;
            }

            if (candidate.Metadata is null)
            {
                continue;
            }

            foreach (var metadataValue in candidate.Metadata.Values)
            {
                if (ContainsIgnoreCase(metadataValue, entity))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<RetrievalCandidate> MergeCandidates(
        IReadOnlyList<RetrievalCandidate> baseCandidates,
        IReadOnlyList<RetrievalCandidate> expandedCandidates)
    {
        var merged = new Dictionary<string, RetrievalCandidate>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in baseCandidates.Concat(expandedCandidates))
        {
            var candidateIdentity = $"{candidate.Source}\u001f{candidate.Key}";
            if (!merged.TryGetValue(candidateIdentity, out var existing) || candidate.Score > existing.Score)
            {
                merged[candidateIdentity] = candidate;
            }
        }

        return SortCandidates(merged.Values);
    }

    private static List<RetrievalCandidate> SortCandidates(IEnumerable<RetrievalCandidate> candidates)
        => candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Source, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Key, StringComparer.Ordinal)
            .ToList();

    private static string NormalizeScopeName(string scope)
        => string.IsNullOrWhiteSpace(scope) ? "global" : scope.Trim();

    private static string? ResolveNamespace(RetrievalCandidate candidate)
    {
        if (TryGetMetadataValue(candidate.Metadata, "namespace", out var metadataNamespace))
        {
            return metadataNamespace;
        }

        var separatorIndex = candidate.Key.IndexOfAny(['/', ':']);
        return separatorIndex > 0 ? candidate.Key[..separatorIndex] : null;
    }

    private static bool TryGetMetadataValue(
        IReadOnlyDictionary<string, string>? metadata,
        string key,
        out string value)
    {
        value = string.Empty;

        if (metadata is null || !metadata.TryGetValue(key, out var rawValue) || string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        value = rawValue.Trim();
        return true;
    }

    private static bool ContainsIgnoreCase(string text, string value)
        => text.Contains(value, StringComparison.OrdinalIgnoreCase);
}

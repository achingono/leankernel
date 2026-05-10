using Microsoft.Extensions.Logging;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Engagement;

/// <summary>
/// Decorator-based authorization service.
/// Checks actions against EngagementRules.AutonomyScope.
/// </summary>
public sealed class ActionAuthorizer : IActionAuthorizer
{
    private readonly EngagementRules _rules;
    private readonly ILogger<ActionAuthorizer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionAuthorizer" /> class.
    /// </summary>
    /// <param name="rules">The engagement rules that define autonomy boundaries.</param>
    /// <param name="logger">The logger used for authorization decisions.</param>
    public ActionAuthorizer(EngagementRules rules, ILogger<ActionAuthorizer> logger)
    {
        _rules = rules;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<AuthorizationResult> AuthorizeAsync(string actionType, CancellationToken ct)
    {
        var autonomy = _rules.Autonomy;
        
        // Check against NeverDo list (highest priority)
        if (autonomy.NeverDo.Contains(actionType, StringComparer.OrdinalIgnoreCase))
        {
            var result = new AuthorizationResult
            {
                IsAuthorized = false,
                ActionType = actionType,
                Reason = "Action is explicitly forbidden"
            };
            
            _logger.LogWarning("Action denied (never-do): {Action}", actionType);
            return Task.FromResult(result);
        }

        // Check against CanDoWithoutAsking list
        if (autonomy.CanDoWithoutAsking.Contains(actionType, StringComparer.OrdinalIgnoreCase))
        {
            var result = new AuthorizationResult
            {
                IsAuthorized = true,
                ActionType = actionType,
                Reason = "Action is allowed without asking"
            };
            
            _logger.LogInformation("Action allowed (no ask): {Action}", actionType);
            return Task.FromResult(result);
        }

        // Check against MustAskBefore list
        if (autonomy.MustAskBefore.Contains(actionType, StringComparer.OrdinalIgnoreCase))
        {
            var result = new AuthorizationResult
            {
                IsAuthorized = false,
                ActionType = actionType,
                Reason = "User permission required"
            };
            
            _logger.LogInformation("Action requires permission: {Action}", actionType);
            return Task.FromResult(result);
        }

        // Unknown action: deny by default (fail safe)
        var unknownResult = new AuthorizationResult
        {
            IsAuthorized = false,
            ActionType = actionType,
            Reason = "Action type not recognized"
        };
        
        _logger.LogWarning("Action denied (unknown): {Action}", actionType);
        return Task.FromResult(unknownResult);
    }
}

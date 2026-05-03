using Microsoft.Extensions.Logging;

namespace LeanKernel.Host.Services;

/// <summary>
/// Authorizes actions based on engagement rules.
/// </summary>
public interface IActionAuthorizer
{
    /// <summary>
    /// Check if an action is authorized.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(string actionType, CancellationToken ct);
}

/// <summary>
/// Result of an authorization check.
/// </summary>
public sealed class AuthorizationResult
{
    public required bool IsAuthorized { get; init; }
    public string? Reason { get; init; }
    public required string ActionType { get; init; }
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Decorator-based authorization service.
/// Checks actions against EngagementRules.AutonomyScope.
/// </summary>
public sealed class ActionAuthorizer : IActionAuthorizer
{
    private readonly EngagementRules _rules;
    private readonly ILogger<ActionAuthorizer> _logger;

    public ActionAuthorizer(EngagementRules rules, ILogger<ActionAuthorizer> logger)
    {
        _rules = rules;
        _logger = logger;
    }

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

/// <summary>
/// Attribute for marking actions that require engagement authorization.
/// Usage: [RequiresEngagementPermission("SendEmail")]
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresEngagementPermissionAttribute : Attribute
{
    public string ActionType { get; }

    public RequiresEngagementPermissionAttribute(string actionType)
    {
        ActionType = actionType;
    }
}

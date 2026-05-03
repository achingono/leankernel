using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Host.Services;

/// <summary>
/// Action filter that enforces engagement rules on methods decorated with [RequiresEngagementPermission].
/// </summary>
public sealed class EngagementAuthorizationFilter : IAsyncActionFilter
{
    private readonly IActionAuthorizer _authorizer;
    private readonly ILogger<EngagementAuthorizationFilter> _logger;

    public EngagementAuthorizationFilter(IActionAuthorizer authorizer, ILogger<EngagementAuthorizationFilter> logger)
    {
        _authorizer = authorizer;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Check if method has RequiresEngagementPermission attribute
        var method = context.ActionDescriptor.DisplayName;
        
        var attribute = (context.ActionDescriptor as Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor)?
            .MethodInfo.GetCustomAttributes(typeof(RequiresEngagementPermissionAttribute), false)
            .FirstOrDefault() as RequiresEngagementPermissionAttribute;

        if (attribute == null)
        {
            // No permission required; proceed normally
            await next();
            return;
        }

        // Authorize the action
        var authorized = await _authorizer.AuthorizeAsync(attribute.ActionName);

        if (!authorized)
        {
            _logger.LogWarning("Engagement authorization denied for action {Action}", attribute.ActionName);
            context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
            return;
        }

        _logger.LogDebug("Engagement authorization granted for action {Action}", attribute.ActionName);
        await next();
    }
}

/// <summary>
/// Decorator attribute to mark actions that require engagement permission.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RequiresEngagementPermissionAttribute : Attribute
{
    public string ActionName { get; }

    public RequiresEngagementPermissionAttribute(string actionName)
    {
        ActionName = actionName;
    }
}

/// <summary>
/// Service to check if an action is authorized under engagement rules.
/// </summary>
public interface IActionAuthorizer
{
    /// <summary>
    /// Check if an action is authorized.
    /// </summary>
    Task<bool> AuthorizeAsync(string actionName);
    
    /// <summary>
    /// Get detailed authorization result with reasoning.
    /// </summary>
    Task<AuthorizationResult> AuthorizeWithDetailsAsync(string actionName);
}

/// <summary>
/// Detailed authorization result.
/// </summary>
public sealed class AuthorizationResult
{
    public required string ActionName { get; init; }
    public required bool Authorized { get; init; }
    public required AuthorizationReason Reason { get; init; }
    public string? Details { get; init; }
}

/// <summary>
/// Reason for authorization decision.
/// </summary>
public enum AuthorizationReason
{
    /// <summary>Action is blocked by NeverDo rule.</summary>
    BlockedByNeverDo,
    
    /// <summary>Action is in CanDoWithoutAsking rule.</summary>
    AllowedByCanDo,
    
    /// <summary>Action is not in any rule (conservative default).</summary>
    NotInRules,
    
    /// <summary>Action requires asking user (not in CanDo).</summary>
    RequiresUserPermission,
}

/// <summary>
/// Default implementation of action authorizer based on engagement rules.
/// </summary>
public sealed class ActionAuthorizer : IActionAuthorizer
{
    private readonly IEngagementRulesProvider _rulesProvider;
    private readonly ILogger<ActionAuthorizer> _logger;

    public ActionAuthorizer(IEngagementRulesProvider rulesProvider, ILogger<ActionAuthorizer> logger)
    {
        _rulesProvider = rulesProvider;
        _logger = logger;
    }

    public async Task<bool> AuthorizeAsync(string actionName)
    {
        var result = await AuthorizeWithDetailsAsync(actionName);
        return result.Authorized;
    }

    public Task<AuthorizationResult> AuthorizeWithDetailsAsync(string actionName)
    {
        var rules = _rulesProvider.GetCurrent();
        
        // Check NeverDo (highest priority)
        if (rules.Autonomy.NeverDo?.Contains(actionName, StringComparer.OrdinalIgnoreCase) == true)
        {
            _logger.LogInformation("Action {Action} denied: in NeverDo list", actionName);
            return Task.FromResult(new AuthorizationResult
            {
                ActionName = actionName,
                Authorized = false,
                Reason = AuthorizationReason.BlockedByNeverDo,
                Details = "Action is explicitly blocked"
            });
        }

        // Check CanDoWithoutAsking (automatic approval)
        if (rules.Autonomy.CanDoWithoutAsking?.Contains(actionName, StringComparer.OrdinalIgnoreCase) == true)
        {
            _logger.LogInformation("Action {Action} approved: in CanDoWithoutAsking list", actionName);
            return Task.FromResult(new AuthorizationResult
            {
                ActionName = actionName,
                Authorized = true,
                Reason = AuthorizationReason.AllowedByCanDo,
                Details = "Action is in auto-approval list"
            });
        }

        // Check MustAskBefore (requires separate permission from user)
        if (rules.Autonomy.MustAskBefore?.Contains(actionName, StringComparer.OrdinalIgnoreCase) == true)
        {
            _logger.LogInformation("Action {Action} requires user permission: in MustAskBefore list", actionName);
            return Task.FromResult(new AuthorizationResult
            {
                ActionName = actionName,
                Authorized = false,
                Reason = AuthorizationReason.RequiresUserPermission,
                Details = "Action requires explicit user permission"
            });
        }

        // Unknown actions default to denied (fail-safe)
        _logger.LogWarning("Action {Action} denied: not in any rule (unknown action)", actionName);
        return Task.FromResult(new AuthorizationResult
        {
            ActionName = actionName,
            Authorized = false,
            Reason = AuthorizationReason.NotInRules,
            Details = "Unknown action; not in engagement rules"
        });
    }
}

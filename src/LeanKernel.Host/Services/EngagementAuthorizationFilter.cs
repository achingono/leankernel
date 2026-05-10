using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Host.Services;

/// <summary>
/// Action filter that enforces engagement rules on methods decorated with [RequiresEngagementPermission].
/// </summary>
public sealed class EngagementAuthorizationFilter : IAsyncActionFilter
{
    private readonly IActionAuthorizer _authorizer;
    private readonly ILogger<EngagementAuthorizationFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EngagementAuthorizationFilter" /> class.
    /// </summary>
    /// <param name="authorizer">The authorizer.</param>
    /// <param name="logger">The logger.</param>
    public EngagementAuthorizationFilter(IActionAuthorizer authorizer, ILogger<EngagementAuthorizationFilter> logger)
    {
        _authorizer = authorizer;
        _logger = logger;
    }

    /// <summary>
    /// Executes the on action execution async operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="next">The next.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Check if method has RequiresEngagementPermission attribute
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
        var result = await _authorizer.AuthorizeAsync(attribute.ActionType, CancellationToken.None);

        if (!result.IsAuthorized)
        {
            _logger.LogWarning("Engagement authorization denied for action {Action}", attribute.ActionType);
            context.Result = new Microsoft.AspNetCore.Mvc.ForbidResult();
            return;
        }

        _logger.LogDebug("Engagement authorization granted for action {Action}", attribute.ActionType);
        await next();
    }
}


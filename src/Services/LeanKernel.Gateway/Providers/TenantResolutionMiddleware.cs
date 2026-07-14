using System.Security.Claims;
using LeanKernel.Entities;
using LeanKernel.Gateway.Configuration;
using Microsoft.Extensions.Options;

namespace LeanKernel.Gateway.Providers;

/// <summary>
/// Middleware that resolves tenant, user, and channel identity eagerly and asynchronously
/// before any request handler executes. Stores resolved values in <see cref="HttpContext.Items"/>
/// so that <see cref="RequestContextPermit"/> can read them synchronously without blocking.
/// </summary>
/// <remarks>
/// Rejects requests with an unresolved or inactive tenant with 401 Unauthorized (C2).
/// Eliminates sync-over-async blocking on the hot request path (M7).
/// Forces ASP.NET session materialization for stable anonymous identity (M6).
/// </remarks>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    /// <summary>Key used to store the resolved tenant identifier in <see cref="HttpContext.Items"/>.</summary>
    public const string TenantKey = "LK.TenantId";

    /// <summary>Key used to store the resolved user identifier in <see cref="HttpContext.Items"/>.</summary>
    public const string UserIdKey = "LK.UserId";

    /// <summary>Key used to store the resolved channel identifier in <see cref="HttpContext.Items"/>.</summary>
    public const string ChannelIdKey = "LK.ChannelId";

    /// <summary>Key used to store the resolved badge in <see cref="HttpContext.Items"/>.</summary>
    public const string BadgeKey = "LK.Badge";

    /// <summary>Marker written to session to force materialization and stabilize the session id.</summary>
    private const string SessionInitMarker = "_lk_init";

    /// <summary>Request paths exempt from tenant resolution (e.g. health probes).</summary>
    private static readonly string[] s_bypassPaths = ["/health"];

    /// <summary>
    /// Resolves the request-scoped identity and invokes the next middleware.
    /// </summary>
    public async Task InvokeAsync(
        HttpContext context,
        IIdentityResolver resolver,
        IOptions<IdentitySettings> identitySettings,
        CancellationToken cancellationToken = default)
    {
        // Skip tenant resolution for health probes and other bypass paths.
        if (ShouldBypass(context.Request.Path))
        {
            await next(context);
            return;
        }

        var host = context.Request.Host.Host;
        var tenant = await resolver.ResolveTenantAsync(host, cancellationToken);

        if (tenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.Items[TenantKey] = tenant.Id;

        Guid userId;
        Badge badge;

        if (context.User?.Identity?.IsAuthenticated == true && context.User is ClaimsPrincipal cp)
        {
            var user = await resolver.ResolveOrCreateUserAsync(cp, cancellationToken);
            userId = user.Id;
            badge = cp.ToBadge();
            badge.Id = user.Id;
        }
        else
        {
            // Force session materialization before using Session.Id as identity key (M6).
            context.Session.SetString(SessionInitMarker, "1");

            var sessionId = context.Session.Id;
            var settings = identitySettings.Value;
            var guestUser = await resolver.ResolveGuestUserAsync(
                tenant.Id, settings.AnonymousUserName, sessionId, cancellationToken);
            userId = guestUser.Id;
            badge = new Badge
            {
                Id = guestUser.Id,
                FullName = settings.AnonymousFullName,
                Email = string.Empty
            };
        }

        context.Items[UserIdKey] = userId;
        context.Items[BadgeKey] = badge;

        var channel = await resolver.ResolveOrCreateChannelAsync(
            ChannelEntity.OpenAiHttpName, cancellationToken);
        context.Items[ChannelIdKey] = channel.Id;

        await next(context);
    }

    private static bool ShouldBypass(PathString path) =>
        s_bypassPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
}

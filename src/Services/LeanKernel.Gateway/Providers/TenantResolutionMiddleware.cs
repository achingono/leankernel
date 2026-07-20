using System.Diagnostics.CodeAnalysis;
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
    private const string ChannelNameClaimType = "lk_channel";
    private const string ChannelTenantIdClaimType = "lk_tenant_id";
    private const string ChannelSenderIssuerClaimType = "lk_sender_iss";
    private const string ChannelSenderSubjectClaimType = "lk_sender_sub";

    /// <summary>Key used to store the resolved tenant identifier in <see cref="HttpContext.Items"/>.</summary>
    public const string TenantKey = "LK.TenantId";

    /// <summary>Key used to store the resolved user identifier in <see cref="HttpContext.Items"/>.</summary>
    public const string UserIdKey = "LK.UserId";

    /// <summary>Key used to store the resolved canonical person identifier in <see cref="HttpContext.Items"/>.</summary>
    public const string PersonIdKey = "LK.PersonId";

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
    /// <param name="context">The HTTP context.</param>
    /// <param name="resolver">The identity resolver.</param>
    /// <param name="identitySettings">The identity configuration options.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [SuppressMessage("Critical Code Smell", "S3776", Justification = "Request identity resolution keeps channel and anonymous flows explicit to preserve security checks.")]
    public async Task InvokeAsync(
        HttpContext context,
        IIdentityResolver resolver,
        IOptions<IdentitySettings> identitySettings)
    {
        var cancellationToken = context.RequestAborted;

        // Skip tenant resolution for health probes and other bypass paths.
        if (ShouldBypass(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (context.User?.Identity?.IsAuthenticated == true
            && context.User is ClaimsPrincipal authenticatedPrincipal
            && HasChannelClaims(authenticatedPrincipal))
        {
            var tenantClaim = authenticatedPrincipal.FindFirst(ChannelTenantIdClaimType)?.Value;
            var channelClaim = authenticatedPrincipal.FindFirst(ChannelNameClaimType)?.Value;

            if (!Guid.TryParse(tenantClaim, out var tenantId) || string.IsNullOrWhiteSpace(channelClaim))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var tenant = await resolver.ResolveTenantByIdAsync(tenantId, cancellationToken);
            var channel = await resolver.ResolveChannelAsync(channelClaim, cancellationToken);
            var user = await resolver.ResolveUserAsync(authenticatedPrincipal, cancellationToken);

            if (tenant is null || channel is null || user is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var senderIssuer = authenticatedPrincipal.FindFirst(ChannelSenderIssuerClaimType)?.Value
                ?? authenticatedPrincipal.FindFirst("iss")?.Value
                ?? string.Empty;
            var senderSubject = authenticatedPrincipal.FindFirst(ChannelSenderSubjectClaimType)?.Value
                ?? authenticatedPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? authenticatedPrincipal.FindFirst("sub")?.Value
                ?? string.Empty;

            var isActiveBinding = await resolver.IsChannelSenderBindingActiveAsync(
                tenant.Id,
                user.Id,
                channel.Id,
                senderIssuer,
                senderSubject,
                cancellationToken);

            if (!isActiveBinding)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            context.Items[TenantKey] = tenant.Id;
            context.Items[UserIdKey] = user.Id;
            context.Items[PersonIdKey] = user.PersonId == Guid.Empty ? user.Id : user.PersonId;
            context.Items[ChannelIdKey] = channel.Id;
            var channelBadge = authenticatedPrincipal.ToBadge();
            channelBadge.Id = user.Id;
            context.Items[BadgeKey] = channelBadge;

            await next(context);
            return;
        }

        var host = context.Request.Host.Host;
        var hostTenant = await resolver.ResolveTenantAsync(host, cancellationToken);

        if (hostTenant is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.Items[TenantKey] = hostTenant.Id;

        Guid userId;
        Guid personId;
        Badge badge;

        if (context.User?.Identity?.IsAuthenticated == true && context.User is ClaimsPrincipal cp)
        {
            var user = await resolver.ResolveOrCreateUserAsync(cp, cancellationToken);
            userId = user.Id;
            personId = user.PersonId == Guid.Empty ? user.Id : user.PersonId;
            badge = cp.ToBadge();
            badge.Id = user.Id;
        }
        else
        {
            context.Session.SetString(SessionInitMarker, "1");

            var sessionId = context.Session.Id;
            var settings = identitySettings.Value;
            var guestUser = await resolver.ResolveGuestUserAsync(
                hostTenant.Id, settings.AnonymousUserName, sessionId, cancellationToken);
            userId = guestUser.Id;
            personId = guestUser.PersonId == Guid.Empty ? guestUser.Id : guestUser.PersonId;
            badge = new Badge
            {
                Id = guestUser.Id,
                FullName = settings.AnonymousFullName,
                Email = string.Empty
            };
        }

        context.Items[UserIdKey] = userId;
        context.Items[PersonIdKey] = personId;
        context.Items[BadgeKey] = badge;

        var openAiChannel = await resolver.ResolveOrCreateChannelAsync(
            ChannelEntity.OpenAiHttpName, cancellationToken);
        context.Items[ChannelIdKey] = openAiChannel.Id;

        await next(context);
    }

    private static bool ShouldBypass(PathString path) =>
        s_bypassPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    private static bool HasChannelClaims(ClaimsPrincipal principal) =>
        principal.HasClaim(claim => claim.Type == ChannelNameClaimType || claim.Type == ChannelTenantIdClaimType);
}
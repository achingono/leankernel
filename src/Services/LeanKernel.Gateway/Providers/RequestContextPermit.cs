using System.Security.Claims;
using LeanKernel;
using LeanKernel.Entities;
using LeanKernel.Gateway.Requests;

namespace LeanKernel.Gateway.Providers;

/// <summary>
/// Resolves <see cref="IPermit"/> from values pre-populated in <see cref="HttpContext.Items"/>
/// by <see cref="TenantResolutionMiddleware"/>. All identity resolution is performed
/// asynchronously in the middleware; this class only reads already-resolved values,
/// eliminating sync-over-async blocking on the hot request path (M7 / S1).
/// </summary>
public sealed class RequestContextPermit(
    IPrincipalAccessor principalAccessor,
    IHostNameAccessor hostNameAccessor,
    IHttpContextAccessor httpContextAccessor) : IPermit
{
    private readonly Lazy<ClaimsPrincipal?> _claimsPrincipal = new(() => principalAccessor.Principal as ClaimsPrincipal);

    private HttpContext? Ctx => httpContextAccessor.HttpContext;

    /// <inheritdoc />
    public string HostName => hostNameAccessor.HostName;

    /// <inheritdoc />
    public bool IsAuthenticated =>
        _claimsPrincipal.Value?.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public string? SessionId => Ctx?.Session?.Id;

    /// <inheritdoc />
    public Guid UserId =>
        Ctx?.Items[TenantResolutionMiddleware.UserIdKey] is Guid uid ? uid : Guid.Empty;

    /// <inheritdoc />
    public Guid PersonId =>
        Ctx?.Items[TenantResolutionMiddleware.PersonIdKey] is Guid pid
            ? pid
            : UserId;

    /// <inheritdoc />
    public Guid TenantId =>
        Ctx?.Items[TenantResolutionMiddleware.TenantKey] is Guid tid ? tid : Guid.Empty;

    /// <inheritdoc />
    public Guid ChannelId =>
        Ctx?.Items[TenantResolutionMiddleware.ChannelIdKey] is Guid cid ? cid : Guid.Empty;

    /// <inheritdoc />
    public Badge Badge =>
        Ctx?.Items[TenantResolutionMiddleware.BadgeKey] is Badge badge
            ? badge
            : new Badge
            {
                Id = Guid.Empty,
                FullName = Constants.Identity.SystemName,
                Email = Constants.Identity.SystemEmail
            };
}

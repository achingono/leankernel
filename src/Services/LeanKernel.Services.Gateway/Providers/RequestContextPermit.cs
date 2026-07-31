using System.Security.Claims;

using LeanKernel.Entities;
using LeanKernel.Services.Gateway.Requests;

namespace LeanKernel.Services.Gateway.Providers;

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
        this._claimsPrincipal.Value?.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public string? SessionId => this.Ctx?.Session?.Id;

    /// <inheritdoc />
    public Guid UserId =>
        this.Ctx?.Items[Constants.Http.ContextItems.UserIdKey] is Guid uid ? uid : Guid.Empty;

    /// <inheritdoc />
    public Guid PersonId =>
        this.Ctx?.Items[Constants.Http.ContextItems.PersonIdKey] is Guid pid
            ? pid
            : this.UserId;

    /// <inheritdoc />
    public Guid TenantId =>
        this.Ctx?.Items[Constants.Http.ContextItems.TenantKey] is Guid tid ? tid : Guid.Empty;

    /// <inheritdoc />
    public Guid ChannelId =>
        this.Ctx?.Items[Constants.Http.ContextItems.ChannelIdKey] is Guid cid ? cid : Guid.Empty;

    /// <inheritdoc />
    public Badge Badge =>
        this.Ctx?.Items[Constants.Http.ContextItems.BadgeKey] is Badge badge
            ? badge
            : new Badge
            {
                Id = Guid.Empty,
                FullName = "System",
                Email = "system@leankernel.local",
            };
}
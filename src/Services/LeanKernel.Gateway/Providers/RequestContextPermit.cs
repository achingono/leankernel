using System.Security.Claims;
using LeanKernel;
using LeanKernel.Entities;
using LeanKernel.Gateway.Configuration;
using LeanKernel.Gateway.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Gateway.Providers;

/// <summary>
/// Resolves <see cref="IPermit"/> from the current HTTP request's host, principal, and session,
/// backed by persisted tenant, user, and channel entities.
/// </summary>
public sealed class RequestContextPermit(
    IPrincipalAccessor principalAccessor,
    IHostNameAccessor hostNameAccessor,
    IHttpContextAccessor httpContextAccessor,
    IServiceProvider serviceProvider,
    IOptions<IdentitySettings> identitySettings) : IPermit
{
    private readonly Lazy<ClaimsPrincipal?> _claimsPrincipal = new(() =>
        principalAccessor.Principal as ClaimsPrincipal);

    private Guid? _resolvedTenantId;
    private Guid? _resolvedUserId;
    private Guid? _resolvedChannelId;
    private Badge? _resolvedBadge;
    private bool _resolving;

    public string HostName => hostNameAccessor.HostName;

    public bool IsAuthenticated =>
        _claimsPrincipal.Value?.Identity?.IsAuthenticated == true;

    public string? SessionId =>
        httpContextAccessor.HttpContext?.Session?.Id;

    public Guid UserId
    {
        get
        {
            EnsureResolved();
            return _resolvedUserId ?? Guid.Empty;
        }
    }

    public Guid TenantId
    {
        get
        {
            EnsureResolved();
            return _resolvedTenantId ?? Guid.Empty;
        }
    }

    public Guid ChannelId
    {
        get
        {
            EnsureResolved();
            return _resolvedChannelId ?? Guid.Empty;
        }
    }

    public Badge Badge
    {
        get
        {
            EnsureResolved();
            return _resolvedBadge ?? new Badge
            {
                Id = UserId,
                FullName = "Anonymous",
                Email = string.Empty
            };
        }
    }

    private void EnsureResolved()
    {
        if (_resolvedTenantId.HasValue)
            return;

        if (_resolving)
            return;

        _resolving = true;
        try
        {
            if (httpContextAccessor.HttpContext is null)
            {
                _resolvedTenantId = Guid.Empty;
                _resolvedUserId = Guid.Empty;
                _resolvedChannelId = Guid.Empty;
                _resolvedBadge = new Badge
                {
                    Id = Guid.Empty,
                    FullName = "System",
                    Email = "system@leankernel.local"
                };
                return;
            }

            var identityResolver = serviceProvider.GetRequiredService<IIdentityResolver>();
            var ct = CancellationToken.None;
            var hostName = HostName;

            // Resolve tenant from host
            var tenant = identityResolver.ResolveTenantAsync(hostName, ct).GetAwaiter().GetResult();
            _resolvedTenantId = tenant?.Id ?? Guid.Empty;

            // Resolve user from principal or create guest
            if (IsAuthenticated && _claimsPrincipal.Value is { } cp)
            {
                var user = identityResolver.ResolveOrCreateUserAsync(cp, ct).GetAwaiter().GetResult();
                _resolvedUserId = user.Id;
                _resolvedBadge = cp.ToBadge();
                _resolvedBadge.Id = user.Id;
            }
            else
            {
                var guestUser = identityResolver.ResolveGuestUserAsync(
                    _resolvedTenantId.Value,
                    identitySettings.Value.AnonymousUserName,
                    SessionId ?? Guid.NewGuid().ToString("N"),
                    ct).GetAwaiter().GetResult();
                _resolvedUserId = guestUser.Id;
                _resolvedBadge = new Badge
                {
                    Id = guestUser.Id,
                    FullName = identitySettings.Value.AnonymousFullName,
                    Email = string.Empty
                };
            }

            // Resolve channel for OpenAI HTTP surface
            var channel = identityResolver.ResolveOrCreateChannelAsync(
                ChannelEntity.OpenAiHttpName, ct).GetAwaiter().GetResult();
            _resolvedChannelId = channel.Id;
        }
        finally
        {
            _resolving = false;
        }
    }
}

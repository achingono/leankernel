using System.Security.Claims;
using LeanKernel;
using LeanKernel.Entities;
using LeanKernel.Gateway.Requests;

namespace LeanKernel.Gateway.Identity;

/// <summary>
/// Resolves <see cref="IPermit"/> from the current HTTP request's host, principal, and session.
/// </summary>
public sealed class RequestContextPermit(
    IPrincipalAccessor principalAccessor,
    IHostNameAccessor hostNameAccessor,
    IHttpContextAccessor httpContextAccessor) : IPermit
{
    private readonly Lazy<ClaimsPrincipal?> _claimsPrincipal = new(() =>
        principalAccessor.Principal as ClaimsPrincipal);

    public string HostName => hostNameAccessor.HostName;

    public bool IsAuthenticated =>
        _claimsPrincipal.Value?.Identity?.IsAuthenticated == true;

    public string? SessionId =>
        httpContextAccessor.HttpContext?.Session?.Id;

    public Guid UserId { get; set; } = Guid.Empty;

    public Guid TenantId { get; set; } = Guid.Empty;

    public Guid ChannelId { get; set; } = Guid.Empty;

    public Badge Badge
    {
        get
        {
            if (_claimsPrincipal.Value is { } cp)
                return cp.ToBadge();

            return new Badge
            {
                Id = UserId,
                FullName = "Anonymous",
                Email = string.Empty
            };
        }
    }
}

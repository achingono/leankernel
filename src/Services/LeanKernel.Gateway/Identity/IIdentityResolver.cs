using System.Security.Claims;
using LeanKernel.Entities;

namespace LeanKernel.Gateway.Identity;

/// <summary>
/// Resolves persisted tenant, user, and channel entities from request inputs.
/// </summary>
public interface IIdentityResolver
{
    /// <summary>
    /// Resolves a <see cref="TenantEntity"/> from the normalized request host name.
    /// Returns null if no tenant is found for the host.
    /// </summary>
    Task<TenantEntity?> ResolveTenantAsync(string hostName, CancellationToken ct = default);

    /// <summary>
    /// Resolves or creates a <see cref="UserEntity"/> from an authenticated claims principal.
    /// Looks up by issuer + subject; creates a new user if not found.
    /// </summary>
    Task<UserEntity> ResolveOrCreateUserAsync(ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>
    /// Resolves or creates a guest <see cref="UserEntity"/> for anonymous requests within a tenant.
    /// </summary>
    Task<UserEntity> ResolveGuestUserAsync(Guid tenantId, string anonymousUserName, CancellationToken ct = default);

    /// <summary>
    /// Resolves or creates the canonical <see cref="ChannelEntity"/> for the OpenAI HTTP surface.
    /// </summary>
    Task<ChannelEntity> ResolveOrCreateChannelAsync(string channelName, CancellationToken ct = default);
}

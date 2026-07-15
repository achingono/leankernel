using System.Security.Claims;

namespace LeanKernel.Entities;

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
    /// Resolves a <see cref="TenantEntity"/> by id.
    /// Returns null if not found or inactive.
    /// </summary>
    Task<TenantEntity?> ResolveTenantByIdAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Resolves or creates a <see cref="UserEntity"/> from an authenticated claims principal.
    /// Looks up by issuer + subject; creates a new user if not found.
    /// </summary>
    Task<UserEntity> ResolveOrCreateUserAsync(ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>
    /// Resolves an existing <see cref="UserEntity"/> from an authenticated claims principal.
    /// Looks up by issuer + subject and does not create new users.
    /// </summary>
    Task<UserEntity?> ResolveUserAsync(ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>
    /// Resolves or creates a guest <see cref="UserEntity"/> for an anonymous session within a tenant.
    /// The <paramref name="sessionId"/> is used as the unique subject identifier so that all
    /// requests sharing the same ASP.NET anonymous session resolve to the same database user.
    /// </summary>
    Task<UserEntity> ResolveGuestUserAsync(Guid tenantId, string anonymousUserName, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Resolves or creates the canonical <see cref="ChannelEntity"/> for the OpenAI HTTP surface.
    /// </summary>
    Task<ChannelEntity> ResolveOrCreateChannelAsync(string channelName, CancellationToken ct = default);

    /// <summary>
    /// Resolves an existing channel by name.
    /// Returns null if not found.
    /// </summary>
    Task<ChannelEntity?> ResolveChannelAsync(string channelName, CancellationToken ct = default);

    /// <summary>
    /// Validates that a pre-provisioned sender binding exists and is active.
    /// </summary>
    Task<bool> IsChannelSenderBindingActiveAsync(
        Guid tenantId,
        Guid userId,
        Guid channelId,
        string issuer,
        string subject,
        CancellationToken ct = default);
}

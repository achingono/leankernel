namespace LeanKernel.Entities;

using System.Security.Claims;

/// <summary>
/// Resolves persisted tenant, user, and channel entities from request inputs.
/// </summary>
public interface IIdentityResolver
{
    /// <summary>
    /// Resolves a <see cref="TenantEntity"/> from the normalized request host name.
    /// Returns null if no tenant is found for the host.
    /// </summary>
    /// <param name="hostName">The normalized request host name.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<TenantEntity?> ResolveTenantAsync(string hostName, CancellationToken ct = default);

    /// <summary>
    /// Resolves a <see cref="TenantEntity"/> by id.
    /// Returns null if not found or inactive.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<TenantEntity?> ResolveTenantByIdAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>
    /// Resolves or creates a <see cref="UserEntity"/> from an authenticated claims principal.
    /// Looks up by issuer + subject; creates a new user if not found.
    /// </summary>
    /// <param name="principal">The authenticated claims principal.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<UserEntity> ResolveOrCreateUserAsync(ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>
    /// Resolves an existing <see cref="UserEntity"/> from an authenticated claims principal.
    /// Looks up by issuer + subject and does not create new users.
    /// </summary>
    /// <param name="principal">The authenticated claims principal.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<UserEntity?> ResolveUserAsync(ClaimsPrincipal principal, CancellationToken ct = default);

    /// <summary>
    /// Resolves or creates a guest <see cref="UserEntity"/> for an anonymous session within a tenant.
    /// The <paramref name="sessionId"/> is used as the unique subject identifier so that all
    /// requests sharing the same ASP.NET anonymous session resolve to the same database user.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="anonymousUserName">The anonymous user name.</param>
    /// <param name="sessionId">The ASP.NET anonymous session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<UserEntity> ResolveGuestUserAsync(Guid tenantId, string anonymousUserName, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Resolves or creates the canonical <see cref="ChannelEntity"/> for the OpenAI HTTP surface.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChannelEntity> ResolveOrCreateChannelAsync(string channelName, CancellationToken ct = default);

    /// <summary>
    /// Resolves an existing channel by name.
    /// Returns null if not found.
    /// </summary>
    /// <param name="channelName">The channel name.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ChannelEntity?> ResolveChannelAsync(string channelName, CancellationToken ct = default);

    /// <summary>
    /// Validates that a pre-provisioned sender binding exists and is active.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="channelId">The channel identifier.</param>
    /// <param name="issuer">The token issuer.</param>
    /// <param name="subject">The token subject.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> IsChannelSenderBindingActiveAsync(
        Guid tenantId,
        Guid userId,
        Guid channelId,
        string issuer,
        string subject,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves the canonical person identifier for a persisted user.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Guid> ResolvePersonIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Links two users to the same canonical person within a tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="sourceUserId">The source user identifier.</param>
    /// <param name="targetUserId">The target user identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Guid> LinkUsersAsync(Guid tenantId, Guid sourceUserId, Guid targetUserId, CancellationToken ct = default);

    /// <summary>
    /// Unlinks a user from a shared canonical person, re-isolating that user.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UnlinkUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}
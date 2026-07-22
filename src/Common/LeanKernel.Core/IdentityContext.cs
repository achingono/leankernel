namespace LeanKernel;

using LeanKernel.Entities;

/// <summary>
/// Canonical identity context providing all identity dimensions resolved for the current request.
/// Preserves the existing split between person-scoped memory (<see cref="PersonId"/>),
/// user-scoped transcript/session ownership (<see cref="UserId"/>), and anonymous session
/// isolation (<see cref="SessionId"/>).
/// </summary>
public sealed record IdentityContext
{
    /// <summary>
    /// Gets the tenant identifier resolved from the request host.
    /// Authoritative for data partitioning across all entities.
    /// </summary>
    public Guid TenantId { get; init; }

    /// <summary>
    /// Gets the person identifier for cross-channel memory partitioning.
    /// Resolved from <c>UserEntity.PersonId</c> (defaults to <c>UserEntity.Id</c>).
    /// </summary>
    public Guid PersonId { get; init; }

    /// <summary>
    /// Gets the user identifier for transcript/session ownership.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the channel identifier for the current HTTP surface.
    /// </summary>
    public Guid ChannelId { get; init; }

    /// <summary>
    /// Gets the ASP.NET session identifier for anonymous session isolation,
    /// or <c>null</c> for authenticated users.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the normalized request host name.
    /// </summary>
    public string HostName { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the current request is from an authenticated principal.
    /// </summary>
    public bool IsAuthenticated { get; init; }

    /// <summary>
    /// Gets the audit badge describing the current identity.
    /// </summary>
    public Badge Badge { get; init; } = new();

    /// <summary>
    /// Creates a canonical <see cref="IdentityContext"/> from the request-scoped <see cref="IPermit"/>.
    /// </summary>
    /// <param name="permit">The request-scoped permit.</param>
    /// <returns>An identity context reflecting the current request identity.</returns>
    public static IdentityContext FromPermit(IPermit permit) => new()
    {
        TenantId = permit.TenantId,
        PersonId = permit.PersonId,
        UserId = permit.UserId,
        ChannelId = permit.ChannelId,
        SessionId = permit.SessionId,
        HostName = permit.HostName,
        IsAuthenticated = permit.IsAuthenticated,
        Badge = permit.Badge,
    };
}
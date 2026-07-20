namespace LeanKernel;

using LeanKernel.Entities;

/// <summary>
/// Resolved request-scoped identity context providing tenant, person, user, and channel partitioning keys.
/// </summary>
public interface IPermit
{
    /// <summary>
    /// Gets the canonical person identifier used for cross-channel memory partitioning.
    /// </summary>
    Guid PersonId { get; }

    /// <summary>
    /// Gets the canonical user identifier (persisted <c>UserEntity.Id</c>).
    /// </summary>
    Guid UserId { get; }

    /// <summary>
    /// Gets the canonical tenant identifier resolved from the request host.
    /// </summary>
    Guid TenantId { get; }

    /// <summary>
    /// Gets the canonical channel identifier for the current HTTP surface.
    /// </summary>
    Guid ChannelId { get; }

    /// <summary>
    /// Gets the normalized request host name.
    /// </summary>
    string HostName { get; }

    /// <summary>
    /// Gets a value indicating whether the current request is from an authenticated principal.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the ASP.NET session identifier for anonymous isolation fallback, or <c>null</c> for authenticated users.
    /// </summary>
    string? SessionId { get; }

    /// <summary>
    /// Gets the audit badge describing the current identity.
    /// </summary>
    Badge Badge { get; }

    /// <summary>
    /// Gets the legacy identifier that returns <see cref="UserId"/>.
    /// </summary>
    Guid Id => this.UserId;
}
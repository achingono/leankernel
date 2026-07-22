namespace LeanKernel.Entities;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Represents a persisted user identity resolved from an external principal or anonymous session.
/// </summary>
public class UserEntity : IAuditable, IRecyclable, IEntity
{
    /// <summary>
    /// Gets or sets the unique identifier for the user.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the email address of the user.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the username for the user.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the first name of the user.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the last name of the user.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full name of the user.
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the preferred username of the user.
    /// </summary>
    public string PreferredUserName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the locale of the user.
    /// </summary>
    public string Locale { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the IANA time zone of the user.
    /// </summary>
    public string TimeZone { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the organization of the user.
    /// </summary>
    public string Organization { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the roles JSON array.
    /// </summary>
    public string RolesJson { get; set; } = "[]";

    /// <summary>
    /// Gets or sets the groups JSON array.
    /// </summary>
    public string GroupsJson { get; set; } = "[]";

    /// <summary>
    /// Gets or sets the custom claims JSON dictionary.
    /// </summary>
    public string CustomClaimsJson { get; set; } = "{}";

    /// <summary>
    /// Gets or sets a value indicating whether the user account is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user account is locked out.
    /// </summary>
    public bool IsLockedOut { get; set; }

    /// <summary>
    /// Gets or sets the date and time of the user's last activity.
    /// </summary>
    public DateTime? LastActivity { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the user was created.
    /// </summary>
    [Required]
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// Gets or sets the badge of the user who created the user.
    /// </summary>
    [Required]
    public Badge CreatedBy { get; set; } = default!;

    /// <summary>
    /// Gets or sets the date and time when the user was last updated.
    /// </summary>
    public DateTime? UpdatedOn { get; set; }

    /// <summary>
    /// Gets or sets the badge of the user who last updated the user.
    /// </summary>
    public Badge? UpdatedBy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user is deleted.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Gets or sets the identity provider issuer (e.g., OIDC issuer URL).
    /// Combined with <see cref="Subject"/>, uniquely identifies an external principal.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the external subject identifier (e.g., OIDC sub claim).
    /// Combined with <see cref="Issuer"/>, uniquely identifies an external principal.
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this is a system-created guest user for anonymous requests.
    /// </summary>
    public bool IsGuest { get; set; }

    /// <summary>
    /// Gets or sets the canonical person identifier used to link channel-native identities.
    /// Defaults to <see cref="Id"/> for unlinked users.
    /// </summary>
    public Guid PersonId { get; set; }

    /// <summary>
    /// Gets or sets the sessions associated with this user.
    /// </summary>
    public virtual ICollection<SessionEntity> Sessions { get; set; } = new List<SessionEntity>();

    /// <summary>
    /// Gets or sets sender bindings associated with this user.
    /// </summary>
    public virtual ICollection<ChannelSenderBindingEntity> ChannelSenderBindings { get; set; } = new List<ChannelSenderBindingEntity>();
}
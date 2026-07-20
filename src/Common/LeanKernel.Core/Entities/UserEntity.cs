using System.ComponentModel.DataAnnotations;

namespace LeanKernel.Entities;

/// <summary>
/// Represents a persisted user identity resolved from an external principal or anonymous session.
/// </summary>
public class UserEntity : IAuditable, IRecyclable
{
    /// <summary>
    /// Unique identifier for the user.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Email address of the user.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Username for the user.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// First name of the user.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Last name of the user.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Full name of the user.
    /// </summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the user account is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Indicates whether the user account is locked out.
    /// </summary>
    public bool IsLockedOut { get; set; }

    /// <summary>
    /// Date and time of the user's last activity.
    /// </summary>
    public DateTime? LastActivity { get; set; }

    /// <summary>
    /// Date and time when the tenant was created.
    /// </summary>
    [Required]
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// Badge of the user who created the tenant.
    /// </summary>
    [Required]
    public Badge CreatedBy { get; set; } = default!;

    /// <summary>
    /// Date and time when the tenant was last updated.
    /// </summary>
    public DateTime? UpdatedOn { get; set; }

    /// <summary>
    /// Badge of the user who last updated the tenant.
    /// </summary>
    public Badge? UpdatedBy { get; set; }

    /// <summary>
    /// Indicates whether the tenant is deleted.
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
    /// Gets or sets whether this is a system-created guest user for anonymous requests.
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
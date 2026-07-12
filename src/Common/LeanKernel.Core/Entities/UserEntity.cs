using LeanKernel.Entities;

namespace LeanKernel.Entities;

public class UserEntity : IEntity, IAuditable, IRecyclable
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsLockedOut { get; set; }
    public DateTime? LastActivity { get; set; }
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public Badge CreatedBy { get; set; } = new();
    public DateTime? UpdatedOn { get; set; }
    public Badge? UpdatedBy { get; set; }
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

    public ICollection<SessionEntity> Sessions { get; set; } = new List<SessionEntity>();
}
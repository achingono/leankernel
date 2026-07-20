using System.ComponentModel.DataAnnotations;

namespace LeanKernel.Entities;

/// <summary>
/// Represents a pre-provisioned sender binding for a channel identity.
/// </summary>
public class ChannelSenderBindingEntity : IEntity
{
    /// <summary>
    /// Gets or sets the unique binding identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the tenant linked to this sender binding.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the user linked to this sender binding.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the channel linked to this sender binding.
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the sender issuer (for example, signal or teams).
    /// </summary>
    [MaxLength(200)]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the sender subject (for example, phone number or AAD object id).
    /// </summary>
    [MaxLength(500)]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the pre-provisioned bearer token used by channel terminals.
    /// </summary>
    public string BearerToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether this binding is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gets or sets when this binding was created.
    /// </summary>
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the tenant navigation.
    /// </summary>
    public virtual TenantEntity Tenant { get; set; } = default!;

    /// <summary>
    /// Gets or sets the user navigation.
    /// </summary>
    public virtual UserEntity User { get; set; } = default!;

    /// <summary>
    /// Gets or sets the channel navigation.
    /// </summary>
    public virtual ChannelEntity Channel { get; set; } = default!;
}
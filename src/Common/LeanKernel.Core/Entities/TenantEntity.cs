namespace LeanKernel.Entities;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Represents a tenant in the system, which can manage multiple groups.
/// </summary>
[DisplayColumn(nameof(Name))]
public class TenantEntity : IAuditable, IRecyclable
{
    /// <summary>
    /// Gets or sets the unique identifier for the tenant.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the name of the tenant.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the description of the tenant.
    /// </summary>
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the hostname associated with the tenant.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string HostName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the tenant is active.
    /// </summary>
    [Required]
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the tenant was created.
    /// </summary>
    [Required]
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// Gets or sets the badge of the user who created the tenant.
    /// </summary>
    [Required]
    public Badge CreatedBy { get; set; } = default!;

    /// <summary>
    /// Gets or sets the date and time when the tenant was last updated.
    /// </summary>
    public DateTime? UpdatedOn { get; set; }

    /// <summary>
    /// Gets or sets the badge of the user who last updated the tenant.
    /// </summary>
    public Badge? UpdatedBy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tenant is deleted.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Gets or sets the sessions associated with this tenant.
    /// </summary>
    public virtual ICollection<SessionEntity> Sessions { get; set; } = new List<SessionEntity>();

    /// <summary>
    /// Gets or sets sender bindings associated with this tenant.
    /// </summary>
    public virtual ICollection<ChannelSenderBindingEntity> ChannelSenderBindings { get; set; } = new List<ChannelSenderBindingEntity>();

    /// <summary>
    /// Gets or sets channel memory policy overrides associated with this tenant.
    /// </summary>
    public virtual ICollection<ChannelMemoryPolicyEntity> ChannelMemoryPolicies { get; set; } = new List<ChannelMemoryPolicyEntity>();
}
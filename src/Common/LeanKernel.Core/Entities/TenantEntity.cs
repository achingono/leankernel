namespace LeanKernel.Entities;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[DisplayColumn(nameof(Name))]
/// <summary>
/// Represents a tenant in the system, which can manage multiple groups.
/// </summary>
public class TenantEntity : IEntity, IAuditable, IRecyclable
{
    /// <summary>
    /// Unique identifier for the tenant.
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    /// <summary>
    /// Name of the tenant.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of the tenant.
    /// </summary>
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Hostname associated with the tenant.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string HostName { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the tenant is active.
    /// </summary>
    [Required]
    public bool IsActive { get; set; }

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
}

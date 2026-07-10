namespace LeanKernel;

using System.ComponentModel.DataAnnotations;
using LeanKernel.Entities;

/// <summary>
/// Provides auditing metadata for entities that track creation and modification details.
/// </summary>
public interface IAuditable : IEntity
{
    /// <summary>
    /// Gets or sets the timestamp when the entity was created.
    /// </summary>
    DateTime CreatedOn { get; set; }

    /// <summary>
    /// Gets or sets the badge describing the identity that created the entity.
    /// </summary>
    Badge CreatedBy { get; set; }

    /// <summary>
    /// Gets or sets the timestamp when the entity was last updated.
    /// </summary>
    DateTime? UpdatedOn { get; set; }

    /// <summary>
    /// Gets or sets the badge describing the identity that last updated the entity.
    /// </summary>
    Badge? UpdatedBy { get; set; }
}

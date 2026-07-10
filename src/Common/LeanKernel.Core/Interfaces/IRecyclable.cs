namespace LeanKernel;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Identifies entities that support soft deletion semantics.
/// </summary>
public interface IRecyclable : IEntity
{
    /// <summary>
    /// Gets or sets a value indicating whether the entity has been soft deleted.
    /// </summary>
    bool IsDeleted { get; set; }
}

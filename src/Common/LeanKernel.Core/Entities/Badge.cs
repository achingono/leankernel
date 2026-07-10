namespace LeanKernel.Entities;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// Represents the audit identity of a user, including identifier, email, and display name.
/// </summary>
[ComplexType]
[DisplayColumn(nameof(FullName))]
public class Badge
{
    /// <summary>
    /// Gets or sets the unique identifier for the user that owns the badge.
    /// </summary>
    [Required]
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the primary email address of the user.
    /// </summary>
    [Required]
    [DataType(DataType.EmailAddress)]
    [MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name rendered in audit trails and UI badges.
    /// </summary>
    [Required]
    [DataType(DataType.Text)]
    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;
}

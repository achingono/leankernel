namespace LeanKernel;

/// <summary>
/// Flags enum identifying the data-partitioning dimensions available for entity scope policies.
/// </summary>
[Flags]
public enum ScopeDimension
{
    /// <summary>
    /// No scope dimension.
    /// </summary>
    None = 0,

    /// <summary>
    /// Tenant-level partitioning.
    /// </summary>
    Tenant = 1,

    /// <summary>
    /// User-level partitioning.
    /// </summary>
    User = 2,

    /// <summary>
    /// Channel-level partitioning.
    /// </summary>
    Channel = 4,
}
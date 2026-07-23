namespace LeanKernel;

/// <summary>
/// Defines the availability scope for document visibility.
/// </summary>
public enum DocumentAvailabilityScope
{
    /// <summary>
    /// Document is discoverable by identities with tenant-level read permission.
    /// </summary>
    Tenant,

    /// <summary>
    /// Document is discoverable only for the uploading/resolved user identity.
    /// </summary>
    User,

    /// <summary>
    /// Document is discoverable only within the current channel visibility set.
    /// </summary>
    Channel,
}

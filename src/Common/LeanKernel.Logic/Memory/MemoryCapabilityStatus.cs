namespace LeanKernel.Logic.Memory;

/// <summary>
/// Possible outcomes of the Memory capability pre-check.
/// </summary>
public enum MemoryCapabilityStatus
{
    /// <summary>All required operations are available.</summary>
    Full,

    /// <summary>Only a subset of operations is available (e.g. get_page or put_page missing).</summary>
    Degraded,

    /// <summary>Memory is unreachable or auth is invalid.</summary>
    Unavailable,

    /// <summary>Local configuration is missing required transport settings.</summary>
    Misconfigured
}
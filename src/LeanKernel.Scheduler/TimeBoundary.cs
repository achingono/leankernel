namespace LeanKernel.Scheduler;

/// <summary>
/// Represents a coarse-grained time boundary within a local day.
/// </summary>
public enum TimeBoundary
{
    /// <summary>
    /// The morning boundary.
    /// </summary>
    Morning,

    /// <summary>
    /// The afternoon boundary.
    /// </summary>
    Afternoon,

    /// <summary>
    /// The evening boundary.
    /// </summary>
    Evening,

    /// <summary>
    /// The night boundary.
    /// </summary>
    Night
}

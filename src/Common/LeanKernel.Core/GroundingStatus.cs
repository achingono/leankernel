namespace LeanKernel.Entities;

/// <summary>
/// Indicates how well an assistant response was grounded in memory evidence.
/// </summary>
public enum GroundingStatus
{
    /// <summary>
    /// The grounding status has not been determined.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The response was fully grounded in retrieved memory evidence.
    /// </summary>
    Grounded = 1,

    /// <summary>
    /// The response was partially grounded, with some content not directly supported by evidence.
    /// </summary>
    Partial = 2,

    /// <summary>
    /// The response was not grounded in any retrieved memory evidence.
    /// </summary>
    Ungrounded = 3,
}

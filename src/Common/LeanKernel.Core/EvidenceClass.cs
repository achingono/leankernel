namespace LeanKernel.Entities;

/// <summary>
/// Represents the type of memory evidence used to ground an assistant response.
/// </summary>
public enum EvidenceClass
{
    /// <summary>
    /// No evidence class has been assigned.
    /// </summary>
    None = 0,

    /// <summary>
    /// The response was grounded in source documents ingested from external systems.
    /// </summary>
    RawDocument = 1,

    /// <summary>
    /// The response used synthesized facts extracted from conversation history.
    /// </summary>
    SynthesizedFact = 2,

    /// <summary>
    /// The response relied on structured pattern pages from memory.
    /// </summary>
    PatternPage = 3,

    /// <summary>
    /// The response was informed by recent conversation transcript context.
    /// </summary>
    Transcript = 4,
}

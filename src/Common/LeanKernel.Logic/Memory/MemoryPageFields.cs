namespace LeanKernel.Logic.Memory;

/// <summary>
/// Defines the canonical 5W1H field names used in memory pages.
/// </summary>
public static class MemoryPageFields
{
    /// <summary>
    /// The ordered set of supported 5W1H fields.
    /// </summary>
    public static readonly string[] FiveWOneH = ["Who", "What", "When", "Where", "Why", "How"];

    /// <summary>
    /// The supported 5W1H fields as a hash set for fast membership checks.
    /// </summary>
    public static readonly HashSet<string> FiveWOneHSet =
        ["Who", "What", "When", "Where", "Why", "How"];

    /// <summary>
    /// Normalizes an arbitrary dimension value to one of the supported canonical names.
    /// </summary>
    /// <param name="value">The dimension value to normalize.</param>
    /// <returns>The normalized dimension name.</returns>
    public static string NormalizeDimension(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "who" => "who",
            "what" => "what",
            "when" => "when",
            "where" => "where",
            "why" => "why",
            "how" => "how",
            _ => "what"
        };
    }
}

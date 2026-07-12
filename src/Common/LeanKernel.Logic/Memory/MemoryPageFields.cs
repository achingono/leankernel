namespace LeanKernel.Logic.Memory;

public static class MemoryPageFields
{
    public static readonly string[] FiveWOneH = ["Who", "What", "When", "Where", "Why", "How"];

    public static readonly HashSet<string> FiveWOneHSet =
        ["Who", "What", "When", "Where", "Why", "How"];

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

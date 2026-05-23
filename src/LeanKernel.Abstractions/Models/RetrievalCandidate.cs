namespace LeanKernel.Abstractions.Models;

public sealed record RetrievalCandidate
{
    public required string Key { get; init; }
    public required string Content { get; init; }
    public required string Source { get; init; }
    public double Score { get; init; }
    public int TokenCount { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

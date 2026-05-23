namespace LeanKernel.Abstractions.Models;

public sealed record ContextAdmissionRecord
{
    public required string Key { get; init; }
    public required string Source { get; init; }
    public double Score { get; init; }
    public int TokenCount { get; init; }
    public bool Admitted { get; init; }
    public string? ExclusionReason { get; init; }
}

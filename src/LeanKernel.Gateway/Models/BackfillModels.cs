namespace LeanKernel.Gateway;

public sealed record BackfillRequest
{
    public required string SourceDirectory { get; init; }
    public string Filter { get; init; } = "*.*";
    public bool Recursive { get; init; } = true;
    public List<string>? Tags { get; init; }
    public int MaxConcurrency { get; init; } = 2;
    public bool DryRun { get; init; }
}

public sealed record BackfillResponse
{
    public int DocumentsIngested { get; init; }
    public bool DryRun { get; init; }
    public string? Message { get; init; }
}

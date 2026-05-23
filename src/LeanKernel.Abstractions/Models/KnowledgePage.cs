namespace LeanKernel.Abstractions.Models;

public sealed record KnowledgePage
{
    public required string Key { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public IReadOnlyList<string>? LinkedPages { get; init; }
}

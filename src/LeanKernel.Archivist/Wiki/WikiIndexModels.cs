namespace LeanKernel.Archivist.Wiki;

/// <summary>
/// Persisted metadata projection for indexed wiki lookup.
/// </summary>
public sealed record WikiIndex
{
    /// <summary>
    /// Gets or sets the schema version.
    /// </summary>
    public int Version { get; init; } = 2;
    /// <summary>
    /// Gets or sets the build timestamp.
    /// </summary>
    public DateTimeOffset BuiltAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Gets or sets indexed wiki entries.
    /// </summary>
    public List<WikiIndexEntry> Entries { get; init; } = [];
    /// <summary>
    /// Gets or sets per-dimension fact pointers.
    /// </summary>
    public Dictionary<string, List<WikiFactPointer>> FactPointers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Indexed metadata for a single wiki entry.
/// </summary>
public sealed record WikiIndexEntry
{
    /// <summary>
    /// Gets or sets entry id.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Gets or sets dimension.
    /// </summary>
    public required string Dimension { get; init; }
    /// <summary>
    /// Gets or sets subject.
    /// </summary>
    public required string Subject { get; init; }
    /// <summary>
    /// Gets or sets normalized subject.
    /// </summary>
    public required string NormalizedSubject { get; init; }
    /// <summary>
    /// Gets or sets summary.
    /// </summary>
    public string? Summary { get; init; }
    /// <summary>
    /// Gets or sets aliases.
    /// </summary>
    public List<string> Aliases { get; init; } = [];
    /// <summary>
    /// Gets or sets tags.
    /// </summary>
    public List<string> Tags { get; init; } = [];
    /// <summary>
    /// Gets or sets relative file path.
    /// </summary>
    public required string FilePath { get; init; }
    /// <summary>
    /// Gets or sets fact count.
    /// </summary>
    public int FactCount { get; init; }
    /// <summary>
    /// Gets or sets max confidence.
    /// </summary>
    public double MaxConfidence { get; init; }
    /// <summary>
    /// Gets or sets last confirmed timestamp.
    /// </summary>
    public DateTimeOffset LastConfirmed { get; init; }
    /// <summary>
    /// Gets or sets sources.
    /// </summary>
    public List<string> Sources { get; init; } = [];
    /// <summary>
    /// Gets or sets fact keys.
    /// </summary>
    public List<string> FactKeys { get; init; } = [];
}

/// <summary>
/// Pointer from a dimension bucket to an entry fact.
/// </summary>
public sealed record WikiFactPointer
{
    /// <summary>
    /// Gets or sets fact key.
    /// </summary>
    public required string FactKey { get; init; }
    /// <summary>
    /// Gets or sets entry id.
    /// </summary>
    public required string EntryId { get; init; }
}

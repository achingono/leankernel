namespace LeanKernel.Host.Models.Routing;

/// <summary>A single field-level model limit drift entry.</summary>
public sealed record DriftEntry(
    string Provider,
    string ModelId,
    string ModelName,
    string Field,
    double? OldValue,
    double? NewValue);

/// <summary>The full drift report returned by the preview endpoint.</summary>
public sealed record DriftReport(
    string GeneratedAt,
    int TotalChanges,
    List<DriftEntry> Changes)
{
    /// <summary>Set when the drift check could not complete (e.g. script not found).</summary>
    public string? Error { get; init; }
}

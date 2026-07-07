namespace LeanKernel.Persistence.Entities;

/// <summary>
/// Represents a persisted file fingerprint for deduplication.
/// </summary>
public sealed class DocumentFingerprintEntity
{
    /// <summary>
    /// Gets or sets the unique fingerprint string (normalizedPath|size|lastWriteUtc).
    /// </summary>
    public required string Fingerprint { get; set; }

    /// <summary>
    /// Gets or sets the normalized file path.
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// Gets or sets the file size in bytes at time of recording.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Gets or sets when the fingerprint was recorded.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
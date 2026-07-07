namespace LeanKernel.Abstractions.Interfaces;

/// <summary>
/// Provides file fingerprint checking and recording for deduplication.
/// </summary>
public interface IDocumentFingerprintService
{
    /// <summary>
    /// Returns true if the given file fingerprint has already been processed successfully.
    /// </summary>
    Task<bool> IsKnownFingerprintAsync(string fingerprint, CancellationToken ct = default);

    /// <summary>
    /// Records a fingerprint as successfully processed.
    /// </summary>
    Task RecordFingerprintAsync(string fingerprint, string filePath, long fileSize, CancellationToken ct = default);

    /// <summary>
    /// Computes a normalized fingerprint string for a file path.
    /// Format: normalizedPath|size|lastWriteUtcTicks
    /// </summary>
    string ComputeFingerprint(string filePath);

    /// <summary>
    /// Removes a fingerprint (for force-reingest scenarios).
    /// </summary>
    Task RemoveFingerprintAsync(string fingerprint, CancellationToken ct = default);
}
namespace LeanKernel.Logic.Diagnostics;

using LeanKernel.Entities;

/// <summary>
/// Collects and buffers diagnostic entries for subsequent consumption and persistence.
/// </summary>
public interface IDiagnosticsCollector
{
    /// <summary>
    /// Captures a diagnostic entry into the collector's buffer.
    /// </summary>
    /// <param name="entry">The diagnostic entry to capture.</param>
    void Capture(DiagnosticEntry entry);

    /// <summary>
    /// Consumes and returns all buffered diagnostic entries, clearing the buffer.
    /// </summary>
    /// <returns>A read-only list of captured diagnostic entries.</returns>
    IReadOnlyList<DiagnosticEntry> Consume();
}
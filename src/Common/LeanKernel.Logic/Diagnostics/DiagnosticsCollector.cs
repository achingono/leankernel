namespace LeanKernel.Logic.Diagnostics;

using System.Collections.Concurrent;
using System.Diagnostics;

using LeanKernel.Entities;

/// <summary>
/// Collects and buffers diagnostic entries using a concurrent queue, emitting OpenTelemetry activities.
/// </summary>
public sealed class DiagnosticsCollector : IDiagnosticsCollector
{
    private ConcurrentQueue<DiagnosticEntry> _entries = new();

    /// <inheritdoc />
    public void Capture(DiagnosticEntry entry)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        using var activity = DiagnosticsActivities.Source.StartActivity(entry.Category, ActivityKind.Internal);
        if (activity is not null)
        {
            if (!string.IsNullOrWhiteSpace(entry.CorrelationId))
            {
                activity.SetTag("correlation.id", entry.CorrelationId);
            }

            if (entry.TurnId is not null)
            {
                activity.SetTag("turn.id", entry.TurnId);
            }

            activity.SetTag("diagnostics.source", entry.Source);
        }

        _entries.Enqueue(entry);
    }

    /// <inheritdoc />
    public IReadOnlyList<DiagnosticEntry> Consume()
    {
        var snapshot = Interlocked.Exchange(ref _entries, new ConcurrentQueue<DiagnosticEntry>());
        return snapshot.ToArray();
    }
}
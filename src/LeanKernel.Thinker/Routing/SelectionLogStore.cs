using System.Collections.Concurrent;
using LeanKernel.Core.Enums;

namespace LeanKernel.Thinker.Routing;

/// <summary>
/// In-memory ring buffer of recent SelectionResult records (capacity: 10,000).
/// Used by the routing observability dashboard to show live metrics without
/// requiring a persistent store. Not durable across restarts by design.
/// Thread-safe via ConcurrentQueue and interlocked trimming.
/// </summary>
public sealed class SelectionLogStore
{
    private const int MaxCapacity = 10_000;

    private readonly ConcurrentQueue<SelectionResult> _queue = new();

    /// <summary>Records a routing decision into the ring buffer.</summary>
    public void Record(SelectionResult result)
    {
        _queue.Enqueue(result);

        // Trim oldest entries when we exceed capacity.
        while (_queue.Count > MaxCapacity)
            _queue.TryDequeue(out _);
    }

    /// <summary>Returns a snapshot of all buffered records, newest last.</summary>
    public IReadOnlyList<SelectionResult> GetAll()
        => _queue.ToArray();

    /// <summary>
    /// Returns all records whose timestamp falls within the last <paramref name="window"/>.
    /// </summary>
    public IReadOnlyList<SelectionResult> GetSince(TimeSpan window)
    {
        var cutoff = DateTimeOffset.UtcNow - window;
        return _queue
            .Where(r => r.Timestamp >= cutoff)
            .ToArray();
    }

    // ─── Aggregation helpers used by the dashboard ────────────────────────────

    /// <summary>
    /// Returns total count of free vs. paid decisions within the window.
    /// </summary>
    public (int Free, int Paid) GetFreeVsPaidCounts(TimeSpan window)
    {
        var records = GetSince(window);
        var free = records.Count(r => r.CostBucket == "free");
        var paid = records.Count(r => r.CostBucket == "paid");
        return (free, paid);
    }

    /// <summary>
    /// Returns escalation rate per complexity tier (percentage of requests where
    /// QualityGateTriggered=true or AttemptCount>1).
    /// </summary>
    public IReadOnlyDictionary<string, double> GetEscalationRateByTier(TimeSpan window)
    {
        var records = GetSince(window);
        return records
            .GroupBy(r => r.Complexity.ToString())
            .ToDictionary(
                g => g.Key,
                g => g.Count() == 0 ? 0d : g.Count(r => r.QualityGateTriggered || r.AttemptCount > 1) / (double)g.Count() * 100.0);
    }

    /// <summary>
    /// Returns p50/p95/p99 latencies in milliseconds within the window.
    /// Returns (0,0,0) when there are no records.
    /// </summary>
    public (long P50, long P95, long P99) GetLatencyPercentiles(TimeSpan window)
    {
        var latencies = GetSince(window)
            .Select(r => r.LatencyMs)
            .OrderBy(x => x)
            .ToArray();

        if (latencies.Length == 0)
            return (0, 0, 0);

        return (
            Percentile(latencies, 50),
            Percentile(latencies, 95),
            Percentile(latencies, 99));
    }

    /// <summary>Complexity distribution (percentage of total) within the window.</summary>
    public IReadOnlyDictionary<TaskComplexity, double> GetComplexityDistribution(TimeSpan window)
    {
        var records = GetSince(window);
        if (records.Count == 0)
            return Enum.GetValues<TaskComplexity>().ToDictionary(c => c, _ => 0d);

        return records
            .GroupBy(r => r.Complexity)
            .ToDictionary(g => g.Key, g => g.Count() / (double)records.Count * 100.0);
    }

    /// <summary>Selection reason breakdown (count per reason tag) within the window.</summary>
    public IReadOnlyDictionary<string, int> GetSelectionReasonCounts(TimeSpan window)
    {
        var records = GetSince(window);
        return records
            .GroupBy(r => CoarseReason(r.SelectionReason))
            .ToDictionary(g => g.Key, g => g.Count());
    }

    // ─── Internal helpers ─────────────────────────────────────────────────────

    private static long Percentile(long[] sorted, int percentile)
    {
        var idx = (int)Math.Ceiling(percentile / 100.0 * sorted.Length) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
    }

    /// <summary>
    /// Normalises raw reason strings (which may include detail suffixes) into broad categories.
    /// e.g. "escalation:quality_gate(too_short)" → "quality_escalation"
    /// </summary>
    private static string CoarseReason(string reason) => reason switch
    {
        "free_first" => "free_first",
        var r when r.StartsWith("escalation:quality_gate") => "quality_escalation",
        var r when r.StartsWith("fallback:transient") => "transient_fallback",
        var r when r.StartsWith("spend_guard") => "spend_guard_fallback",
        _ => "other"
    };
}

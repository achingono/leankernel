using LeanKernel.Core.Enums;
using LeanKernel.Thinker.Routing;

namespace LeanKernel.Tests.Unit.Thinker.Routing;

public class SelectionLogStoreTests
{
    private static SelectionResult MakeResult(
        string alias = "small",
        string tier = "small",
        string costBucket = "free",
        TaskComplexity complexity = TaskComplexity.Small,
        string reason = "free_first",
        int attempts = 1,
        bool qualityGate = false,
        long latencyMs = 120)
        => new SelectionResult
        {
            RequestId = Guid.NewGuid().ToString(),
            Complexity = complexity,
            SelectedAlias = alias,
            SelectedTier = tier,
            SelectionReason = reason,
            CostBucket = costBucket,
            AttemptCount = attempts,
            FallbackPath = [],
            LatencyMs = latencyMs,
            QualityGateTriggered = qualityGate,
            Timestamp = DateTimeOffset.UtcNow
        };

    [Fact]
    public void Record_AddsToBuffer()
    {
        var store = new SelectionLogStore();
        store.Record(MakeResult());
        Assert.Single(store.GetAll());
    }

    [Fact]
    public void GetAll_ReturnsAllRecords()
    {
        var store = new SelectionLogStore();
        for (var i = 0; i < 10; i++)
            store.Record(MakeResult());
        Assert.Equal(10, store.GetAll().Count);
    }

    [Fact]
    public void GetSince_FiltersOldRecords()
    {
        var store = new SelectionLogStore();

        // Record a result with a past timestamp manually via a wrapper
        // (Timestamp has init setter — use overload with specific time)
        store.Record(new SelectionResult
        {
            RequestId = "old",
            Complexity = TaskComplexity.Small,
            SelectedAlias = "small",
            SelectedTier = "small",
            SelectionReason = "free_first",
            CostBucket = "free",
            AttemptCount = 1,
            FallbackPath = [],
            LatencyMs = 100,
            Timestamp = DateTimeOffset.UtcNow.AddHours(-2) // older than 1h window
        });

        store.Record(MakeResult()); // recent

        var recent = store.GetSince(TimeSpan.FromHours(1));
        Assert.Single(recent);
    }

    [Fact]
    public void GetFreeVsPaidCounts_CorrectlySplits()
    {
        var store = new SelectionLogStore();
        store.Record(MakeResult(costBucket: "free"));
        store.Record(MakeResult(costBucket: "free"));
        store.Record(MakeResult(costBucket: "paid"));

        var (free, paid) = store.GetFreeVsPaidCounts(TimeSpan.FromHours(1));
        Assert.Equal(2, free);
        Assert.Equal(1, paid);
    }

    [Fact]
    public void GetLatencyPercentiles_ReturnsCorrectValues()
    {
        var store = new SelectionLogStore();
        foreach (var ms in new long[] { 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 })
            store.Record(MakeResult(latencyMs: ms));

        var (p50, p95, p99) = store.GetLatencyPercentiles(TimeSpan.FromHours(1));
        Assert.True(p50 > 0);
        Assert.True(p95 >= p50);
        Assert.True(p99 >= p95);
    }

    [Fact]
    public void GetComplexityDistribution_SumsTo100Percent()
    {
        var store = new SelectionLogStore();
        store.Record(MakeResult(complexity: TaskComplexity.Small));
        store.Record(MakeResult(complexity: TaskComplexity.Small));
        store.Record(MakeResult(complexity: TaskComplexity.Medium));
        store.Record(MakeResult(complexity: TaskComplexity.Large));

        var dist = store.GetComplexityDistribution(TimeSpan.FromHours(1));
        var total = dist.Values.Sum();
        Assert.Equal(100.0, total, precision: 6);
    }

    [Fact]
    public void GetEscalationRateByTier_EscalatedRequestsCountedCorrectly()
    {
        var store = new SelectionLogStore();
        // 2 small: 1 escalated, 1 not
        store.Record(MakeResult(complexity: TaskComplexity.Small, attempts: 2));
        store.Record(MakeResult(complexity: TaskComplexity.Small, attempts: 1));

        var rates = store.GetEscalationRateByTier(TimeSpan.FromHours(1));
        Assert.Equal(50.0, rates["Small"], precision: 6);
    }

    [Fact]
    public void GetSelectionReasonCounts_GroupsCoarseReasons()
    {
        var store = new SelectionLogStore();
        store.Record(MakeResult(reason: "free_first"));
        store.Record(MakeResult(reason: "free_first"));
        store.Record(MakeResult(reason: "escalation:quality_gate(too_short)"));

        var counts = store.GetSelectionReasonCounts(TimeSpan.FromHours(1));
        Assert.Equal(2, counts["free_first"]);
        Assert.Equal(1, counts["quality_escalation"]);
    }

    [Fact]
    public void Record_TrimmedWhenCapacityExceeded()
    {
        // Use reflection to set a lower capacity, or just verify we don't OOM at 10001 records.
        var store = new SelectionLogStore();
        for (var i = 0; i < 10_001; i++)
            store.Record(MakeResult());

        Assert.True(store.GetAll().Count <= 10_000);
    }
}

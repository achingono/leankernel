using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Thinker.Routing;

namespace LeanKernel.Tests.Unit.Thinker.Routing;

public class TaskComplexityScorerTests
{
    private static TaskComplexityScorer CreateScorer(
        int smallMaxTokens = 4_000,
        int smallMaxConstraints = 3,
        int mediumMaxTokens = 16_000,
        int mediumMaxConstraints = 8)
    {
        var config = Options.Create(new LeanKernelConfig
        {
            Routing = new RoutingConfig
            {
                SmallMaxTokens = smallMaxTokens,
                SmallMaxConstraints = smallMaxConstraints,
                MediumMaxTokens = mediumMaxTokens,
                MediumMaxConstraints = mediumMaxConstraints,
            }
        });
        return new TaskComplexityScorer(config);
    }

    [Fact]
    public void Score_ShortPromptFewConstraints_ReturnsSmall()
    {
        var scorer = CreateScorer();
        var prompt = "What is the capital of France?";
        var (complexity, _, _) = scorer.Score(prompt);
        Assert.Equal(TaskComplexity.Small, complexity);
    }

    [Fact]
    public void Score_LongPrompt_ReturnsMediumOrLarge()
    {
        var scorer = CreateScorer();
        // Generate a prompt with > 4000 estimated tokens (roughly 3000+ words).
        var words = string.Join(" ", Enumerable.Repeat("word", 3200));
        var (complexity, _, _) = scorer.Score(words);
        Assert.True(complexity >= TaskComplexity.Medium);
    }

    [Fact]
    public void Score_ManyConstraints_ReturnsMedium()
    {
        var scorer = CreateScorer();
        var prompt = "Please do the following:\n1. Step one\n2. Step two\n3. Step three\n4. Step four\n5. Step five";
        var (complexity, _, constraintCount) = scorer.Score(prompt);
        Assert.True(constraintCount >= 4);
        Assert.True(complexity >= TaskComplexity.Medium);
    }

    [Fact]
    public void Score_ExcessiveConstraints_ReturnsLarge()
    {
        var scorer = CreateScorer();
        // 9+ constraints → large
        var lines = string.Join("\n", Enumerable.Range(1, 9).Select(i => $"{i}. Constraint {i}"));
        var (complexity, _, constraintCount) = scorer.Score(lines);
        Assert.True(constraintCount >= 9);
        Assert.Equal(TaskComplexity.Large, complexity);
    }

    [Fact]
    public void EstimateTokens_Empty_ReturnsZero()
    {
        Assert.Equal(0, TaskComplexityScorer.EstimateTokens(""));
        Assert.Equal(0, TaskComplexityScorer.EstimateTokens("   "));
    }

    [Fact]
    public void EstimateTokens_SingleWord_ReturnsPositive()
    {
        var tokens = TaskComplexityScorer.EstimateTokens("hello");
        Assert.True(tokens > 0);
    }

    [Fact]
    public void ExistingContextTokens_PushesIntoHigherTier()
    {
        var scorer = CreateScorer(smallMaxTokens: 4_000);
        var shortPrompt = "Hello";  // Very few tokens on its own
        var (smallComplexity, _, _) = scorer.Score(shortPrompt, 0);
        var (largeComplexity, _, _) = scorer.Score(shortPrompt, 5_000);

        Assert.Equal(TaskComplexity.Small, smallComplexity);
        Assert.True(largeComplexity >= TaskComplexity.Medium);
    }
}

public class ProviderHealthTrackerTests
{
    [Fact]
    public void IsOnCooldown_NewAlias_ReturnsFalse()
    {
        var tracker = new ProviderHealthTracker();
        Assert.False(tracker.IsOnCooldown("small"));
    }

    [Fact]
    public void MarkCooledDown_ThenIsOnCooldown_ReturnsTrue()
    {
        var tracker = new ProviderHealthTracker(TimeSpan.FromMinutes(5));
        tracker.MarkCooledDown("medium");
        Assert.True(tracker.IsOnCooldown("medium"));
    }

    [Fact]
    public void IsOnCooldown_AfterExpiry_ReturnsFalse()
    {
        var tracker = new ProviderHealthTracker(TimeSpan.FromMilliseconds(1));
        tracker.MarkCooledDown("large");
        System.Threading.Thread.Sleep(10);
        Assert.False(tracker.IsOnCooldown("large"));
    }

    [Fact]
    public void GetSnapshot_IncludesActiveCooldowns()
    {
        var tracker = new ProviderHealthTracker(TimeSpan.FromMinutes(5));
        tracker.MarkCooledDown("small");
        var snapshot = tracker.GetSnapshot();
        Assert.True(snapshot.ContainsKey("small"));
    }
}

public class ResponseQualityGateTests
{
    private static ResponseQualityGate CreateGate(int minLength = 80, double minCoverage = 0.80)
    {
        var config = Options.Create(new LeanKernelConfig
        {
            Routing = new RoutingConfig
            {
                QualityMinOutputLength = minLength,
                QualityMinConstraintCoverage = minCoverage,
            }
        });
        return new ResponseQualityGate(config);
    }

    [Fact]
    public void Passes_EmptyResponse_ReturnsFalse()
    {
        var gate = CreateGate();
        Assert.False(gate.Passes("", "prompt", 0, out var reason));
        Assert.Equal("empty_output", reason);
    }

    [Fact]
    public void Passes_TooShortResponse_ReturnsFalse()
    {
        var gate = CreateGate(minLength: 80);
        Assert.False(gate.Passes("Short.", "Write a detailed explanation", 0, out var reason));
        Assert.NotNull(reason);
        Assert.Contains("output_too_short", reason);
    }

    [Fact]
    public void Passes_AdequateResponse_ReturnsTrue()
    {
        var gate = CreateGate(minLength: 20);
        var response = new string('x', 25);
        Assert.True(gate.Passes(response, "Explain something", 0, out var reason));
        Assert.Null(reason);
    }

    [Fact]
    public void Passes_TersePrompt_SkipsLengthCheck()
    {
        var gate = CreateGate(minLength: 200);
        Assert.True(gate.Passes("Yes.", "Answer yes or no", 0, out _));
    }

    [Fact]
    public void Passes_LowConstraintCount_SkipsCoverageCheck()
    {
        var gate = CreateGate();
        // Only 2 constraints → coverage check is skipped.
        var response = new string('a', 100);
        Assert.True(gate.Passes(response, "Do this and that", 2, out _));
    }
}

public class SpendGuardTests
{
    private static SpendGuard CreateGuard(int softLimit = 0, int hardLimit = 0)
    {
        var config = Options.Create(new LeanKernelConfig
        {
            Routing = new RoutingConfig
            {
                SpendGuard = new SpendGuardConfig
                {
                    DailyPaidRequestSoftLimit = softLimit,
                    DailyPaidRequestHardLimit = hardLimit,
                }
            }
        });
        return new SpendGuard(config, NullLogger<SpendGuard>.Instance);
    }

    [Fact]
    public void ILeanKernelLimitActive_NoLimit_ReturnsFalse()
    {
        var guard = CreateGuard(hardLimit: 0);
        Assert.False(guard.ILeanKernelLimitActive());
    }

    [Fact]
    public void ILeanKernelLimitActive_BelowLimit_ReturnsFalse()
    {
        var guard = CreateGuard(hardLimit: 10);
        guard.RecordPaidRequest();
        Assert.False(guard.ILeanKernelLimitActive());
    }

    [Fact]
    public void ILeanKernelLimitActive_AtLimit_ReturnsTrue()
    {
        var guard = CreateGuard(hardLimit: 2);
        guard.RecordPaidRequest();
        guard.RecordPaidRequest();
        Assert.True(guard.ILeanKernelLimitActive());
    }

    [Fact]
    public void CurrentCount_TracksRequests()
    {
        var guard = CreateGuard();
        Assert.Equal(0, guard.CurrentCount());
        guard.RecordPaidRequest();
        guard.RecordPaidRequest();
        Assert.Equal(2, guard.CurrentCount());
    }
}

public class PolicyModelSelectorTests
{
    private static PolicyModelSelector CreateSelector(
        ProviderHealthTracker? health = null,
        SpendGuard? spend = null,
        int hardLimit = 0)
    {
        var config = Options.Create(new LeanKernelConfig
        {
            Routing = new RoutingConfig
            {
                SmallAlias = "small",
                MediumAlias = "medium",
                LargeAlias = "large",
                SpendGuard = new SpendGuardConfig { DailyPaidRequestHardLimit = hardLimit }
            }
        });
        health ??= new ProviderHealthTracker();
        spend ??= new SpendGuard(config, NullLogger<SpendGuard>.Instance);
        return new PolicyModelSelector(config, health, spend);
    }

    [Fact]
    public void BuildCandidates_Small_IncludesSmallFirst()
    {
        var selector = CreateSelector();
        var candidates = selector.BuildCandidates(TaskComplexity.Small);
        Assert.Equal("small", candidates[0].Alias);
        Assert.False(candidates[0].IsPaid);
    }

    [Fact]
    public void BuildCandidates_AllTiersPresent_ForSmall()
    {
        var selector = CreateSelector();
        var candidates = selector.BuildCandidates(TaskComplexity.Small);
        var aliases = candidates.Select(c => c.Alias).ToList();
        Assert.Contains("small", aliases);
        Assert.Contains("medium", aliases);
        Assert.Contains("large", aliases);
    }

    [Fact]
    public void BuildCandidates_PaidLastAndIsPaid()
    {
        var selector = CreateSelector();
        var candidates = selector.BuildCandidates(TaskComplexity.Small);
        var paid = candidates.LastOrDefault();
        Assert.NotNull(paid);
        Assert.True(paid.IsPaid);
        Assert.Equal("paid", paid.Tier);
    }

    [Fact]
    public void BuildCandidates_SkipsCooledDownAlias()
    {
        var health = new ProviderHealthTracker(TimeSpan.FromMinutes(5));
        health.MarkCooledDown("small");
        var selector = CreateSelector(health: health);
        var candidates = selector.BuildCandidates(TaskComplexity.Small);
        Assert.DoesNotContain(candidates, c => c.Alias == "small" && !c.IsPaid);
    }

    [Fact]
    public void BuildCandidates_HardLimitActive_ExcludesPaidCandidate()
    {
        var config = Options.Create(new LeanKernelConfig
        {
            Routing = new RoutingConfig
            {
                SmallAlias = "small",
                MediumAlias = "medium",
                LargeAlias = "large",
                SpendGuard = new SpendGuardConfig { DailyPaidRequestHardLimit = 1 }
            }
        });
        var spend = new SpendGuard(config, NullLogger<SpendGuard>.Instance);
        spend.RecordPaidRequest(); // hit the limit
        var selector = CreateSelector(spend: spend, hardLimit: 1);
        var candidates = selector.BuildCandidates(TaskComplexity.Small);
        Assert.DoesNotContain(candidates, c => c.IsPaid);
    }
}

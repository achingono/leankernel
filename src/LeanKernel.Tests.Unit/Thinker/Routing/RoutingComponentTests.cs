using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Models;
using LeanKernel.Thinker;
using LeanKernel.Thinker.Routing;
using System.Net;

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

public class ModelRoutingServiceTests
{
    [Fact]
    public async Task RouteAsync_ReturnsFirstAcceptedCandidate()
    {
        var service = CreateService(new RoutingTestChatClient("accepted response"));

        var (response, metadata) = await service.RouteAsync(
            "req-1",
            "Answer the question",
            existingContextTokens: 0,
            systemInstructions: "system",
            tools: null,
            history: null,
            CancellationToken.None);

        Assert.Equal("accepted response", response);
        Assert.Equal("small", metadata.SelectedAlias);
        Assert.Equal("free_first", metadata.SelectionReason);
        Assert.Equal(1, metadata.AttemptCount);
        Assert.False(metadata.QualityGateTriggered);
    }

    [Fact]
    public async Task RouteAsync_TransientFailureFallsBackAndCoolsDownAlias()
    {
        var health = new ProviderHealthTracker(TimeSpan.FromMinutes(5));
        var service = CreateService(
            new RoutingTestChatClient(
                new HttpRequestException("Service unavailable", null, HttpStatusCode.ServiceUnavailable),
                "fallback response"),
            health);

        var (response, metadata) = await service.RouteAsync(
            "req-2",
            "Answer the question",
            existingContextTokens: 0,
            systemInstructions: "system",
            tools: null,
            history: null,
            CancellationToken.None);

        Assert.Equal("fallback response", response);
        Assert.Equal("medium", metadata.SelectedAlias);
        Assert.Equal("fallback:transient_503", metadata.SelectionReason);
        Assert.Equal(2, metadata.AttemptCount);
        Assert.True(health.IsOnCooldown("small"));
        Assert.Equal(["small", "medium"], metadata.FallbackPath);
    }

    [Fact]
    public async Task RouteAsync_QualityFailureEscalatesToNextCandidate()
    {
        var service = CreateService(
            new RoutingTestChatClient("short", "this response is long enough to pass the quality gate"),
            enableQualityEscalation: true,
            qualityMinOutputLength: 20);

        var (response, metadata) = await service.RouteAsync(
            "req-3",
            "Explain the result",
            existingContextTokens: 0,
            systemInstructions: "system",
            tools: null,
            history: null,
            CancellationToken.None);

        Assert.Equal("this response is long enough to pass the quality gate", response);
        Assert.Equal("medium", metadata.SelectedAlias);
        Assert.StartsWith("escalation:quality_gate(output_too_short", metadata.SelectionReason);
        Assert.Equal(2, metadata.AttemptCount);
        Assert.True(metadata.QualityGateTriggered);
    }

    [Fact]
    public async Task RouteAsync_IncludesConversationHistory()
    {
        var chatClient = new RoutingTestChatClient("accepted response");
        var service = CreateService(chatClient);
        var history = new List<ConversationTurn>
        {
            new()
            {
                Role = "user",
                Content = "The document is named Chingono Alfero.pdf.",
                Timestamp = DateTimeOffset.UtcNow
            },
            new()
            {
                Role = "assistant",
                Content = "I will remember that filename.",
                Timestamp = DateTimeOffset.UtcNow
            }
        };

        await service.RouteAsync(
            "req-4",
            "Read that document now.",
            existingContextTokens: 0,
            systemInstructions: "system",
            tools: null,
            history: history,
            CancellationToken.None);

        var messages = chatClient.LastMessages;
        Assert.Contains(messages, m => m.Role == ChatRole.User && m.Text.Contains("Chingono Alfero.pdf"));
        Assert.Contains(messages, m => m.Role == ChatRole.User && m.Text.Contains("Read that document now."));
    }

    private static ModelRoutingService CreateService(
        RoutingTestChatClient chatClient,
        ProviderHealthTracker? health = null,
        bool enableQualityEscalation = false,
        int qualityMinOutputLength = 80)
    {
        var config = Options.Create(new LeanKernelConfig
        {
            Routing = new RoutingConfig
            {
                SmallAlias = "small",
                MediumAlias = "medium",
                LargeAlias = "large",
                MaxProviderAttempts = 4,
                MaxSelectionBudgetMs = 10_000,
                EnableQualityEscalation = enableQualityEscalation,
                QualityMinOutputLength = qualityMinOutputLength
            }
        });
        health ??= new ProviderHealthTracker(TimeSpan.FromMinutes(5));
        var spend = new SpendGuard(config, NullLogger<SpendGuard>.Instance);
        var dependencies = new ModelRoutingDependencies(
            new TaskComplexityScorer(config),
            new PolicyModelSelector(config, health, spend),
            new ResponseQualityGate(config),
            health,
            spend,
            new AgentFactory(chatClient, NullLogger<AgentFactory>.Instance));

        return new ModelRoutingService(
            dependencies,
            config,
            NullLogger<ModelRoutingService>.Instance);
    }

    private sealed class RoutingTestChatClient : IChatClient
    {
        private readonly Queue<object> _responses;

        public RoutingTestChatClient(params object[] responses)
        {
            _responses = new Queue<object>(responses);
        }

        public ChatClientMetadata Metadata => new();

        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IChatClient) ? this : null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            var next = _responses.Count > 0 ? _responses.Dequeue() : string.Empty;
            if (next is Exception exception)
                throw exception;

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, next.ToString())));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}

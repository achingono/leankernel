using FluentAssertions;
using System.Reflection;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents;
using LeanKernel.Agents.Strategies;
using LeanKernel.Context;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Agents;

public class ContinuationTurnPipelineTests
{
    [Fact]
    public async Task ProcessDetailedAsync_returns_the_inner_response_when_continuation_is_disabled()
    {
        var (pipeline, store, _) = CreatePipeline(
            ["First pass.\n```task-status\n{\"status\":\"in_progress\",\"note\":\"Still working.\"}\n```"],
            continuationConfig: new ContinuationConfig { Enabled = false });

        var response = await pipeline.ProcessDetailedAsync(CreateMessage());

        response.Content.Should().Be("First pass.");
        response.Execution.Should().NotBeNull();
        response.Execution!.ToolInvocationCount.Should().Be(1);
        response.Execution.TaskStatus.Should().NotBeNull();
        response.Execution.TaskStatus!.Status.Should().Be("in_progress");
        store.Turns.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessDetailedAsync_continues_incomplete_tasks_and_records_progress()
    {
        var progressBroker = new TurnProgressBroker();
        var progressEvents = new List<TurnProgressUpdate>();
        using var progressSubscription = progressBroker.Subscribe("session-1", update =>
        {
            progressEvents.Add(update);
            return Task.CompletedTask;
        });

        var (pipeline, store, _) = CreatePipeline(
            [
                "Research complete.\n```task-status\n{\"status\":\"in_progress\",\"note\":\"Drafting the summary next.\"}\n```",
                "Summary complete.\n```task-status\n{\"status\":\"complete\",\"note\":\"Everything is wrapped up.\"}\n```",
            ],
            progressBroker: progressBroker);

        var response = await pipeline.ProcessDetailedAsync(CreateMessage());

        response.Content.Should().Be("Summary complete.");
        response.Execution.Should().NotBeNull();
        response.Execution!.TaskStatus.Should().NotBeNull();
        response.Execution.TaskStatus!.Status.Should().Be("complete");
        progressEvents.Should().Contain(update => update.Kind == TurnProgressKind.ContinuationStarted);
        progressEvents.Should().Contain(update => update.Kind == TurnProgressKind.StatusNote && update.Message == "Drafting the summary next.");
        progressEvents
            .Where(update => update.Kind is TurnProgressKind.ContinuationStarted or TurnProgressKind.StatusNote)
            .Select(update => update.TurnId)
            .Distinct(StringComparer.Ordinal)
            .Should()
            .ContainSingle();
        store.Turns.Should().HaveCount(3);
        store.Turns.Count(entry => string.Equals(entry.Turn.Role, "user", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
        store.Turns.Any(entry =>
                entry.Turn.Metadata is not null
                && entry.Turn.Metadata.TryGetValue("internal_reason", out var reason)
                && string.Equals(reason, "auto_continuation_prompt", StringComparison.OrdinalIgnoreCase))
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task ProcessDetailedAsync_stops_when_the_same_incomplete_response_repeats()
    {
        var (pipeline, store, _) = CreatePipeline(
            [
                "Working on it.\n```task-status\n{\"status\":\"in_progress\",\"note\":\"Still working.\"}\n```",
                "Working on it.\n```task-status\n{\"status\":\"in_progress\",\"note\":\"Still working.\"}\n```",
            ]);

        var response = await pipeline.ProcessDetailedAsync(CreateMessage());

        response.Content.Should().Contain("I've paused because recent continuation rounds produced the same result.");
        response.Execution.Should().NotBeNull();
        response.Execution!.TaskStatus.Should().NotBeNull();
        response.Execution.TaskStatus!.Status.Should().Be("in_progress");
        store.Turns.Should().HaveCount(3);
    }

    [Fact]
    public async Task ProcessDetailedAsync_stops_when_the_auto_continuation_limit_is_reached()
    {
        var (pipeline, store, _) = CreatePipeline(
            ["Working on it.\n```task-status\n{\"status\":\"in_progress\",\"note\":\"Still working.\"}\n```"],
            continuationConfig: new ContinuationConfig
            {
                Enabled = true,
                MaxAutoContinuations = 0
            });

        var response = await pipeline.ProcessDetailedAsync(CreateMessage());

        response.Content.Should().Contain("I've paused here after several continuation rounds.");
        response.Execution.Should().NotBeNull();
        response.Execution!.TaskStatus.Should().NotBeNull();
        response.Execution.TaskStatus!.Status.Should().Be("in_progress");
        store.Turns.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessAsync_returns_the_inner_content_without_additional_wrapping()
    {
        var (pipeline, _, _) = CreatePipeline(
            ["Working on it.\n```task-status\n{\"status\":\"in_progress\",\"note\":\"Still working.\"}\n```"],
            continuationConfig: new ContinuationConfig { Enabled = false });

        var content = await pipeline.ProcessAsync(CreateMessage());

        content.Should().Be("Working on it.");
    }

    [Fact]
    public async Task ProcessDetailedAsync_stops_when_the_maximum_duration_is_exceeded()
    {
        var timeProvider = new SequencedTimeProvider(
            DateTimeOffset.Parse("2025-05-20T10:00:00Z"),
            DateTimeOffset.Parse("2025-05-20T10:02:00Z"));

        var (pipeline, store, _) = CreatePipeline(
            ["Working on it.\n```task-status\n{\"status\":\"in_progress\",\"note\":\"Still working.\"}\n```"],
            continuationConfig: new ContinuationConfig
            {
                Enabled = true,
                MaxTotalDurationSeconds = 0
            },
            timeProvider: timeProvider);

        var response = await pipeline.ProcessDetailedAsync(CreateMessage());

        response.Content.Should().Contain("I've paused here due to turn time limits.");
        response.Execution.Should().NotBeNull();
        response.Execution!.TaskStatus.Should().NotBeNull();
        response.Execution.TaskStatus!.Status.Should().Be("in_progress");
        store.Turns.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessDetailedAsync_stops_when_a_newer_message_preempts_the_turn()
    {
        var (pipeline, store, coordinator) = CreatePipeline(
            ["Working on it.\n```task-status\n{\"status\":\"in_progress\",\"note\":\"Still working.\"}\n```"],
            continuationConfig: new ContinuationConfig
            {
                Enabled = true,
                MaxAutoContinuations = 2
            });

        coordinator.Lease.PreemptionRequested = true;

        var response = await pipeline.ProcessDetailedAsync(CreateMessage());

        response.Content.Should().Contain("I've paused here because a newer message arrived in this chat.");
        response.Execution.Should().NotBeNull();
        response.Execution!.TaskStatus.Should().NotBeNull();
        response.Execution.TaskStatus!.Status.Should().Be("in_progress");
        store.Turns.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessDetailedAsync_stops_when_no_tool_invocations_were_executed()
    {
        var (pipeline, store, _) = CreatePipeline(
            ["Working on it.\n```task-status\n{\"status\":\"in_progress\",\"note\":\"Still working.\"}\n```"],
            toolRegistryTools: Array.Empty<ToolDefinition>());

        var response = await pipeline.ProcessDetailedAsync(CreateMessage());

        response.Content.Should().Be("Working on it.");
        response.Execution.Should().NotBeNull();
        response.Execution!.ToolInvocationCount.Should().Be(0);
        response.Execution.TaskStatus.Should().NotBeNull();
        response.Execution.TaskStatus!.Status.Should().Be("in_progress");
        store.Turns.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessDetailedAsync_returns_the_initial_response_when_the_task_is_complete()
    {
        var (pipeline, store, _) = CreatePipeline(
            ["Done.\n```task-status\n{\"status\":\"complete\",\"note\":\"Everything is wrapped up.\"}\n```"],
            continuationConfig: new ContinuationConfig
            {
                Enabled = true,
                MaxAutoContinuations = 2
            });

        var response = await pipeline.ProcessDetailedAsync(CreateMessage());

        response.Content.Should().Be("Done.");
        response.Execution.Should().NotBeNull();
        response.Execution!.ToolInvocationCount.Should().Be(1);
        response.Execution.TaskStatus.Should().NotBeNull();
        response.Execution.TaskStatus!.Status.Should().Be("complete");
        store.Turns.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessDetailedAsync_stops_when_spend_guard_blocks_continuation()
    {
        var spendGuardService = new Mock<ISpendGuardService>(MockBehavior.Strict);
        spendGuardService
            .Setup(service => service.Evaluate("session-1", ModelTier.Standard, It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new SpendGuardDecision
            {
                Action = SpendGuardAction.Block,
                Reason = "Budget exhausted"
            });

        var (pipeline, store, _) = CreatePipeline(
            ["Working on it.\n```task-status\n{\"status\":\"in_progress\",\"note\":\"Still working.\"}\n```"],
            continuationConfig: new ContinuationConfig
            {
                Enabled = true,
                MaxAutoContinuations = 2
            },
            spendGuardService: spendGuardService.Object);

        var response = await pipeline.ProcessDetailedAsync(CreateMessage());

        response.Content.Should().Contain("Budget exhausted");
        response.Execution.Should().NotBeNull();
        response.Execution!.TaskStatus.Should().NotBeNull();
        response.Execution.TaskStatus!.Status.Should().Be("in_progress");
        store.Turns.Should().HaveCount(2);
        spendGuardService.VerifyAll();
    }

    [Fact]
    public async Task ProcessDetailedAsync_records_cancellation_after_the_initial_response_completes()
    {
        var cts = new CancellationTokenSource();
        var turnEventSink = new Mock<ITurnEventSink>(MockBehavior.Strict);
        turnEventSink
            .Setup(sink => sink.PublishAsync(It.IsAny<TurnEvent>(), It.IsAny<CancellationToken>()))
            .Callback<TurnEvent, CancellationToken>((_, _) => cts.Cancel())
            .Returns(Task.CompletedTask);

        var (pipeline, store, _) = CreatePipeline(
            ["Working on it.\n```task-status\n{\"status\":\"in_progress\",\"note\":\"Still working.\"}\n```"],
            continuationConfig: new ContinuationConfig
            {
                Enabled = true,
                MaxAutoContinuations = 2
            },
            turnEventSink: turnEventSink.Object);

        var response = await pipeline.ProcessDetailedAsync(CreateMessage(), cts.Token);

        response.Content.Should().Be("Working on it.");
        store.Turns.Should().HaveCount(2);
    }

    [Fact]
    public void Private_helper_methods_handle_blank_pause_notes_and_text()
    {
        var type = typeof(ContinuationTurnPipeline);

        var appendPauseNote = type.GetMethod("AppendPauseNote", BindingFlags.NonPublic | BindingFlags.Static);
        appendPauseNote.Should().NotBeNull();
        var response = new AgentResponse { Content = "Done." };
        var noteResult = (AgentResponse)appendPauseNote!.Invoke(null, [response, "   "])!;
        noteResult.Should().BeSameAs(response);

        var normalizeText = type.GetMethod("NormalizeText", BindingFlags.NonPublic | BindingFlags.Static);
        normalizeText.Should().NotBeNull();
        normalizeText!.Invoke(null, ["   "]).Should().Be(string.Empty);
    }

    private static LeanKernelMessage CreateMessage()
        => new()
        {
            Content = "Please implement the requested feature and continue until all steps are complete.",
            SenderId = "user-1",
            ChannelId = "channel-1",
            SessionId = "session-1",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["source"] = "unit-test"
            }
        };

    private static (ContinuationTurnPipeline Pipeline, RecordingSessionStore Store, TestSessionTurnCoordinator Coordinator) CreatePipeline(
        IReadOnlyList<string> responses,
        ContinuationConfig? continuationConfig = null,
        ITurnProgressBroker? progressBroker = null,
        ISpendGuardService? spendGuardService = null,
        IReadOnlyList<ToolDefinition>? toolRegistryTools = null,
        TimeProvider? timeProvider = null,
        Action<AgentStrategyContext>? onStrategyInvoke = null,
        ITurnEventSink? turnEventSink = null)
    {
        var store = new RecordingSessionStore();
        var coordinator = new TestSessionTurnCoordinator();
        var strategy = new QueueingToolStrategy(responses, onStrategyInvoke);
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        var visibleTools = toolRegistryTools ?? [
            new ToolDefinition
            {
                Name = "inspect",
                Description = "Inspect",
                Handler = (_, _) => Task.FromResult(new ToolResult
                {
                    ToolName = "inspect",
                    Success = true,
                    Output = "ok"
                })
            }
        ];

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.IsAny<ToolVisibilityContext>()))
            .Returns(visibleTools);

        var config = new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig
            {
                ContextWindowTokens = 100,
                DefaultModel = "test-model"
            },
            Continuation = continuationConfig ?? new ContinuationConfig(),
        };

        var inner = new TurnPipeline(
            gatekeeper.Object,
            store,
            strategy,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(config),
            NullLogger<TurnPipeline>.Instance,
            turnEventSink: turnEventSink);

        var pipeline = new ContinuationTurnPipeline(
            inner,
            new TaskCompletionEvaluator(Options.Create(config)),
            coordinator,
            store,
            Options.Create(config),
            NullLogger<ContinuationTurnPipeline>.Instance,
            progressBroker,
            spendGuardService: spendGuardService,
            metrics: null,
            timeProvider: timeProvider ?? TimeProvider.System);

        return (pipeline, store, coordinator);
    }

    private sealed class SequencedTimeProvider(params DateTimeOffset[] values) : TimeProvider
    {
        private readonly Queue<DateTimeOffset> _values = new(values);
        private DateTimeOffset _current = values.Length > 0 ? values[^1] : DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow()
        {
            if (_values.Count > 0)
            {
                _current = _values.Dequeue();
            }

            return _current;
        }
    }

    private sealed class RecordingSessionStore : ISessionStore
    {
        public List<(string SessionId, ConversationTurn Turn)> Turns { get; } = [];

        public Task<string> GetOrCreateSessionIdAsync(string channelId, string userId, CancellationToken ct = default)
            => Task.FromResult("session-1");

        public Task AppendTurnAsync(string sessionId, ConversationTurn turn, CancellationToken ct = default)
        {
            Turns.Add((sessionId, turn));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ConversationTurn>> GetHistoryAsync(string sessionId, int maxTurns = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConversationTurn>>([]);

        public Task<bool> SessionBelongsToUserAsync(string sessionId, string userId, CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private sealed class TestSessionTurnCoordinator : ISessionTurnCoordinator
    {
        public TestTurnLease Lease { get; } = new();

        public ValueTask<ITurnLease> BeginTurnAsync(string sessionId, CancellationToken ct = default)
            => ValueTask.FromResult<ITurnLease>(Lease);

        public void NotifyInbound(string sessionId)
        {
        }
    }

    private sealed class TestTurnLease : ITurnLease
    {
        public bool PreemptionRequested { get; set; }

        public ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }

    private sealed class QueueingToolStrategy : IAgentStrategy
    {
        private readonly Queue<string> _responses;
        private readonly Action<AgentStrategyContext>? _onInvoke;

        public QueueingToolStrategy(IEnumerable<string> responses, Action<AgentStrategyContext>? onInvoke = null)
        {
            _responses = new Queue<string>(responses);
            _onInvoke = onInvoke;
        }

        public string Name => "queueing";

        public async Task<string> InvokeAsync(AgentStrategyContext context, CancellationToken ct = default)
        {
            _onInvoke?.Invoke(context);
            if (context.Tools is { Count: > 0 })
            {
                var function = context.Tools[0].GetService<AIFunction>();
                if (function is not null)
                {
                    await function.InvokeAsync(new AIFunctionArguments(), ct).ConfigureAwait(false);
                }
            }

            return _responses.Count > 0 ? _responses.Dequeue() : string.Empty;
        }
    }
}

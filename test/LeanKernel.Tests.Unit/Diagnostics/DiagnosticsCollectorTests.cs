using System.Diagnostics;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace LeanKernel.Tests.Unit.Diagnostics;

public class DiagnosticsCollectorTests
{
    [Fact]
    public async Task RecordContextAdmissionAsync_does_nothing_when_diagnostics_are_disabled()
    {
        var sink = new RecordingDiagnosticsSink();
        var collector = CreateCollector(new DiagnosticsConfig
        {
            Enabled = false,
            PersistToDatabase = true
        }, sink);

        await collector.RecordContextAdmissionAsync(
            "session-1",
            "turn-1",
            [new ContextAdmissionRecord { Key = "doc-1", Source = "gbrain", Admitted = true }]);

        sink.Entries.Should().BeEmpty();
        collector.StartTurnActivity("session-1", "turn-1").Should().BeNull();
    }

    [Fact]
    public async Task RecordContextAdmissionAsync_persists_entries_when_enabled_and_sink_is_available()
    {
        var sink = new RecordingDiagnosticsSink();
        var collector = CreateCollector(new DiagnosticsConfig
        {
            Enabled = true,
            PersistToDatabase = true
        }, sink);

        await collector.RecordContextAdmissionAsync(
            "session-1",
            "turn-1",
            [new ContextAdmissionRecord { Key = "doc-1", Source = "gbrain", Admitted = true },
             new ContextAdmissionRecord { Key = "doc-2", Source = "wiki", Admitted = false }]);

        sink.Entries.Should().ContainSingle();
        sink.Entries[0].Category.Should().Be(DiagnosticCategory.ContextAdmission.ToString());
        var payload = sink.Entries[0].Payload.Should().BeAssignableTo<IReadOnlyList<ContextAdmissionRecord>>().Subject;
        payload.Should().HaveCount(2);
        payload[0].Admitted.Should().BeTrue();
    }

    [Fact]
    public async Task RecordBudgetUsageAsync_persists_entries_when_enabled_and_a_sink_is_available()
    {
        var sink = new RecordingDiagnosticsSink();
        var collector = CreateCollector(new DiagnosticsConfig
        {
            Enabled = true,
            PersistToDatabase = true
        }, sink);
        var usage = new ContextBudgetUsage
        {
            SystemPromptUsed = 5,
            WikiFactsUsed = 3,
            ConversationUsed = 2,
            RetrievalUsed = 4,
            ToolsUsed = 1
        };

        await collector.RecordBudgetUsageAsync("session-1", "turn-1", usage);

        sink.Entries.Should().ContainSingle();
        sink.Entries[0].Category.Should().Be(DiagnosticCategory.BudgetAllocation.ToString());
        sink.Entries[0].Payload.Should().Be(usage);
    }

    [Fact]
    public async Task RecordToolVisibilityAsync_persists_entries_when_enabled_and_sink_is_available()
    {
        var sink = new RecordingDiagnosticsSink();
        var collector = CreateCollector(new DiagnosticsConfig
        {
            Enabled = true,
            PersistToDatabase = true
        }, sink);

        await collector.RecordToolVisibilityAsync(
            "session-1",
            "turn-1",
            ["wiki_search"],
            ["admin_reset"]);

        sink.Entries.Should().ContainSingle();
        sink.Entries[0].Category.Should().Be(DiagnosticCategory.ToolVisibility.ToString());
    }

    [Fact]
    public async Task RecordToolVisibilityAsync_does_not_require_a_sink()
    {
        var collector = CreateCollector(new DiagnosticsConfig
        {
            Enabled = true,
            PersistToDatabase = true
        });

        var act = () => collector.RecordToolVisibilityAsync(
            "session-1",
            "turn-1",
            ["wiki_search"],
            ["admin_reset"]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RecordModelRoutingAsync_persists_the_structured_routing_decision()
    {
        var sink = new RecordingDiagnosticsSink();
        var collector = CreateCollector(new DiagnosticsConfig
        {
            Enabled = true,
            PersistToDatabase = true
        }, sink);
        var decision = new RoutingDecision
        {
            SelectedTier = ModelTier.Standard,
            SelectedModel = "gpt-4o",
            ComplexityScore = 0.42,
            Reason = "standard tier selected",
            Factors = ["message-tokens:100:medium"],
            EscalationAttempt = 0,
        };

        await collector.RecordModelRoutingAsync("session-1", "turn-1", decision);

        sink.Entries.Should().ContainSingle();
        sink.Entries[0].Category.Should().Be(DiagnosticCategory.ModelRouting.ToString());
        sink.Entries[0].Payload.Should().Be(decision);
    }

    [Fact]
    public async Task RecordOrchestrationAsync_persists_the_structured_orchestration_result()
    {
        var sink = new RecordingDiagnosticsSink();
        var collector = CreateCollector(new DiagnosticsConfig
        {
            Enabled = true,
            PersistToDatabase = true
        }, sink);
        var result = new OrchestrationResult
        {
            CoordinatorResponse = "Final answer",
            WorkerContributions =
            [
                new WorkerContribution
                {
                    WorkerName = "researcher",
                    Task = "Investigate Atlas",
                    Response = "Atlas facts",
                    Duration = TimeSpan.FromSeconds(1),
                    Success = true
                }
            ],
            TotalDuration = TimeSpan.FromSeconds(2),
            TotalWorkerInvocations = 1
        };

        await collector.RecordOrchestrationAsync("session-1", "turn-1", result);

        sink.Entries.Should().ContainSingle();
        sink.Entries[0].Category.Should().Be(DiagnosticCategory.Orchestration.ToString());
        sink.Entries[0].Payload.Should().Be(result);
    }

    [Fact]
    public async Task RecordQualityGateAsync_persists_the_structured_quality_result()
    {
        var sink = new RecordingDiagnosticsSink();
        var collector = CreateCollector(new DiagnosticsConfig
        {
            Enabled = true,
            PersistToDatabase = true
        }, sink);
        var result = new QualityGateResult
        {
            Outcome = QualityOutcome.FailedLowCoverage,
            Passed = false,
            FailureReason = "Matched 2 of 4 constraints (0.50).",
            OverallScore = 0.625,
            Checks =
            [
                new QualityCheckResult { CheckName = "empty-response", Passed = true, Score = 1.0 },
                new QualityCheckResult { CheckName = "min-length", Passed = true, Score = 1.0 },
                new QualityCheckResult { CheckName = "refusal-detection", Passed = true, Score = 1.0 },
                new QualityCheckResult { CheckName = "constraint-coverage", Passed = false, Score = 0.5, Details = "Matched 2 of 4 constraints (0.50)." }
            ]
        };

        await collector.RecordQualityGateAsync("session-1", "turn-1", result);

        sink.Entries.Should().ContainSingle();
        sink.Entries[0].Category.Should().Be(DiagnosticCategory.QualityGate.ToString());
        sink.Entries[0].Payload.Should().Be(result);
    }

    [Fact]
    public async Task RecordResponseEnhancementAsync_persists_the_structured_enhancement_result()
    {
        var sink = new RecordingDiagnosticsSink();
        var collector = CreateCollector(new DiagnosticsConfig
        {
            Enabled = true,
            PersistToDatabase = true
        }, sink);
        var result = new EnhancementResult
        {
            OriginalResponse = "draft response",
            EnhancedResponse = "enhanced response",
            WasModified = true,
            Steps =
            [
                new EnhancementStepResult
                {
                    StepName = "knowledge-synthesis",
                    Applied = true,
                    Modified = true,
                    Reason = "Appended source note.",
                    Duration = TimeSpan.FromMilliseconds(5)
                }
            ],
            TotalDuration = TimeSpan.FromMilliseconds(5)
        };

        await collector.RecordResponseEnhancementAsync("session-1", "turn-1", result);

        sink.Entries.Should().ContainSingle();
        sink.Entries[0].Category.Should().Be(DiagnosticCategory.ResponseEnhancement.ToString());
        sink.Entries[0].Payload.Should().Be(result);
    }

    [Fact]
    public void StartTurnActivity_returns_an_activity_when_enabled_and_a_listener_is_registered()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "LeanKernel.Diagnostics",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        var collector = CreateCollector(new DiagnosticsConfig
        {
            Enabled = true,
            PersistToDatabase = false
        });

        using var activity = collector.StartTurnActivity("session-1", "turn-1");

        activity.Should().NotBeNull();
        activity!.DisplayName.Should().Be("Turn");
        activity.GetTagItem("session.id").Should().Be("session-1");
        activity.GetTagItem("turn.id").Should().Be("turn-1");
    }

    [Fact]
    public void AddLeanKernelDiagnostics_registers_the_collector_context_service_and_metrics()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Options.Create(new LeanKernelConfig()));

        services.AddLeanKernelDiagnostics(new DiagnosticsConfig
        {
            Enabled = true,
            PersistToDatabase = false,
            ContextDiagnosticsEnabled = true,
            MaxDiagnosticsPerSession = 100,
        });

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<DiagnosticsCollector>().Should().NotBeNull();
        provider.GetRequiredService<IContextDiagnosticsService>().Should().NotBeNull();
        provider.GetRequiredService<LeanKernelMetrics>().Should().NotBeNull();
    }

    [Fact]
    public void LeanKernelLogEnricher_adds_the_service_name_property()
    {
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration()
            .Enrich.With(new LeanKernelLogEnricher("gateway"))
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("Hello");

        sink.Events.Should().ContainSingle();
        sink.Events[0].Properties["ServiceName"].ToString().Should().Be("\"gateway\"");
    }

    private static DiagnosticsCollector CreateCollector(DiagnosticsConfig config, IDiagnosticsSink? sink = null)
        => new(NullLogger<DiagnosticsCollector>.Instance, Options.Create(config), sink);

    private sealed class RecordingDiagnosticsSink : IDiagnosticsSink
    {
        public List<DiagnosticEntry> Entries { get; } = [];

        public Task RecordAsync(DiagnosticEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DiagnosticEntry>> GetEntriesAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DiagnosticEntry>>(Entries.Where(entry => entry.SessionId == sessionId).ToList());
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
        }
    }
}

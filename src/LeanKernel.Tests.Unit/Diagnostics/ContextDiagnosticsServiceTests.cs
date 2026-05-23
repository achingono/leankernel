using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Diagnostics;

public class ContextDiagnosticsServiceTests
{
    [Fact]
    public async Task Store_and_get_context_diagnostics_returns_the_latest_snapshot_when_turn_id_is_not_supplied()
    {
        var sink = new RecordingDiagnosticsSink();
        var service = CreateService(sink);

        await service.StoreContextDiagnosticsAsync("session-1", "turn-1", CreateSnapshot(timestamp: DateTimeOffset.Parse("2025-05-20T10:00:00Z")));
        await service.StoreContextDiagnosticsAsync("session-1", "turn-2", CreateSnapshot(
            admissions:
            [
                new ContextAdmissionRecord { Key = "wiki-2", Source = "wiki", Score = 0.9, TokenCount = 2, Admitted = true },
                new ContextAdmissionRecord { Key = "doc-2", Source = "gbrain", Score = 0.2, TokenCount = 3, Admitted = false, ExclusionReason = "BudgetExhausted" }
            ],
            timestamp: DateTimeOffset.Parse("2025-05-20T10:05:00Z")));

        var response = await service.GetContextDiagnosticsAsync("session-1");

        response.Should().NotBeNull();
        response!.TurnId.Should().Be("turn-2");
        response.TotalCandidatesConsidered.Should().Be(2);
        response.TotalAdmitted.Should().Be(1);
        response.TotalExcluded.Should().Be(1);
        response.Admissions.Select(admission => admission.Key).Should().Equal("wiki-2", "doc-2");
    }

    [Fact]
    public async Task Get_budget_diagnostics_returns_the_requested_turn()
    {
        var sink = new RecordingDiagnosticsSink();
        var service = CreateService(sink);

        await service.StoreContextDiagnosticsAsync("session-1", "turn-1", CreateSnapshot());
        await service.StoreContextDiagnosticsAsync("session-1", "turn-2", CreateSnapshot(
            budget: new ContextBudget
            {
                TotalTokens = 90,
                SystemPromptBudget = 15,
                WikiFactsBudget = 20,
                RetrievalBudget = 25,
                ConversationBudget = 20,
                ToolsBudget = 10,
            },
            usage: new ContextBudgetUsage
            {
                SystemPromptUsed = 10,
                WikiFactsUsed = 12,
                RetrievalUsed = 9,
                ConversationUsed = 7,
                ToolsUsed = 3,
            },
            totalBudgetTokens: 120,
            responseHeadroomRatio: 0.25));

        var response = await service.GetBudgetDiagnosticsAsync("session-1", "turn-2");

        response.Should().NotBeNull();
        response!.TurnId.Should().Be("turn-2");
        response.TotalBudgetTokens.Should().Be(120);
        response.UsableBudgetTokens.Should().Be(90);
        response.ResponseHeadroomRatio.Should().Be(0.25);
        response.WikiFacts.Allocated.Should().Be(20);
        response.WikiFacts.Used.Should().Be(12);
        response.WikiFacts.UtilizationPercent.Should().Be(60);
    }

    [Fact]
    public async Task Get_history_diagnostics_returns_counts_and_tokens_saved()
    {
        var sink = new RecordingDiagnosticsSink();
        var service = CreateService(sink);

        await service.StoreContextDiagnosticsAsync("session-1", "turn-1", CreateSnapshot(
            history: new HistoryShapingDiagnostics
            {
                TotalTurns = 8,
                VerbatimTurns = 3,
                CompactedTurns = 2,
                SummarizedTurns = 1,
                DroppedTurns = 2,
                TotalTokensBefore = 200,
                TotalTokensAfter = 120,
                BudgetAvailable = 150,
            }));

        var response = await service.GetHistoryDiagnosticsAsync("session-1", "turn-1");

        response.Should().NotBeNull();
        response!.VerbatimTurns.Should().Be(3);
        response.CompactedTurns.Should().Be(2);
        response.SummarizedTurns.Should().Be(1);
        response.DroppedTurns.Should().Be(2);
        response.TokensSaved.Should().Be(80);
    }

    [Fact]
    public async Task Disabled_context_diagnostics_skips_storage_and_reads_as_missing()
    {
        var sink = new RecordingDiagnosticsSink();
        var service = CreateService(sink, new DiagnosticsConfig
        {
            Enabled = true,
            PersistToDatabase = true,
            ContextDiagnosticsEnabled = false,
            MaxDiagnosticsPerSession = 100,
        });

        await service.StoreContextDiagnosticsAsync("session-1", "turn-1", CreateSnapshot());

        sink.Entries.Should().BeEmpty();
        await service.GetContextDiagnosticsAsync("session-1").Should().BeNullAsync();
    }

    [Fact]
    public async Task Invalid_payloads_are_skipped_when_resolving_snapshots()
    {
        var sink = new RecordingDiagnosticsSink();
        sink.Entries.Add(new DiagnosticEntry
        {
            SessionId = "session-1",
            TurnId = "turn-bad",
            Category = DiagnosticCategory.ContextSnapshot.ToString(),
            Payload = "{not valid json}",
            Timestamp = DateTimeOffset.Parse("2025-05-20T10:00:00Z"),
        });
        sink.Entries.Add(new DiagnosticEntry
        {
            SessionId = "session-1",
            TurnId = "turn-good",
            Category = DiagnosticCategory.ContextSnapshot.ToString(),
            Payload = CreateSnapshot(timestamp: DateTimeOffset.Parse("2025-05-20T10:01:00Z")),
            Timestamp = DateTimeOffset.Parse("2025-05-20T10:01:00Z"),
        });
        var service = CreateService(sink);

        var response = await service.GetContextDiagnosticsAsync("session-1");

        response.Should().NotBeNull();
        response!.TurnId.Should().Be("turn-good");
    }

    [Fact]
    public async Task Max_diagnostics_per_session_limits_the_considered_snapshot_window()
    {
        var sink = new RecordingDiagnosticsSink();
        var service = CreateService(sink, new DiagnosticsConfig
        {
            Enabled = true,
            PersistToDatabase = true,
            ContextDiagnosticsEnabled = true,
            MaxDiagnosticsPerSession = 2,
        });

        await service.StoreContextDiagnosticsAsync("session-1", "turn-1", CreateSnapshot(timestamp: DateTimeOffset.Parse("2025-05-20T10:00:00Z")));
        await service.StoreContextDiagnosticsAsync("session-1", "turn-2", CreateSnapshot(timestamp: DateTimeOffset.Parse("2025-05-20T10:01:00Z")));
        await service.StoreContextDiagnosticsAsync("session-1", "turn-3", CreateSnapshot(timestamp: DateTimeOffset.Parse("2025-05-20T10:02:00Z")));

        await service.GetContextDiagnosticsAsync("session-1", "turn-1").Should().BeNullAsync();
        var latest = await service.GetContextDiagnosticsAsync("session-1");
        latest.Should().NotBeNull();
        latest!.TurnId.Should().Be("turn-3");
    }

    private static ContextDiagnosticsService CreateService(
        RecordingDiagnosticsSink sink,
        DiagnosticsConfig? diagnosticsConfig = null,
        LeanKernelConfig? leanKernelConfig = null)
        => new(
            NullLogger<ContextDiagnosticsService>.Instance,
            Options.Create(diagnosticsConfig ?? new DiagnosticsConfig
            {
                Enabled = true,
                PersistToDatabase = true,
                ContextDiagnosticsEnabled = true,
                MaxDiagnosticsPerSession = 100,
            }),
            Options.Create(leanKernelConfig ?? new LeanKernelConfig
            {
                LiteLlm = new LiteLlmConfig
                {
                    ContextWindowTokens = 128,
                },
                Context = new ContextConfig
                {
                    ResponseHeadroomRatio = 0.25,
                },
            }),
            sink);

    private static ContextDiagnosticsSnapshot CreateSnapshot(
        IReadOnlyList<ContextAdmissionRecord>? admissions = null,
        ContextBudgetUsage? usage = null,
        ContextBudget? budget = null,
        HistoryShapingDiagnostics? history = null,
        RetrievalDiagnostics? retrieval = null,
        int totalBudgetTokens = 128,
        double responseHeadroomRatio = 0.25,
        DateTimeOffset? timestamp = null)
        => new()
        {
            Admissions = admissions ??
            [
                new ContextAdmissionRecord { Key = "wiki-1", Source = "wiki", Score = 0.95, TokenCount = 4, Admitted = true },
                new ContextAdmissionRecord { Key = "doc-1", Source = "gbrain", Score = 0.05, TokenCount = 6, Admitted = false, ExclusionReason = "LowRelevanceScore" },
            ],
            BudgetUsage = usage ?? new ContextBudgetUsage
            {
                SystemPromptUsed = 10,
                WikiFactsUsed = 8,
                RetrievalUsed = 6,
                ConversationUsed = 12,
                ToolsUsed = 2,
            },
            Budget = budget ?? new ContextBudget
            {
                TotalTokens = 96,
                SystemPromptBudget = 15,
                WikiFactsBudget = 20,
                RetrievalBudget = 20,
                ConversationBudget = 36,
                ToolsBudget = 5,
            },
            TotalBudgetTokens = totalBudgetTokens,
            ResponseHeadroomRatio = responseHeadroomRatio,
            HistoryDiagnostics = history,
            RetrievalDiagnostics = retrieval ?? new RetrievalDiagnostics
            {
                SessionId = "session-1",
                TurnId = "turn-1",
                TotalConsidered = 2,
                TotalAdmitted = 1,
                TotalExcludedByScope = 0,
                TotalExcludedByScore = 1,
                EffectiveScope = "personal",
                ExpandedEntities = ["atlas"],
            },
            Timestamp = timestamp ?? DateTimeOffset.Parse("2025-05-20T10:00:00Z"),
        };

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
}

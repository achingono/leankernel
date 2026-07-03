using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents;
using LeanKernel.Agents.ToolSelection;
using LeanKernel.Agents.Strategies;
using LeanKernel.Context;
using LeanKernel.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Agents;

public class TurnPipelineTests
{
    [Fact]
    public async Task ProcessDetailedAsync_skips_persisting_internal_continuation_user_turn()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var persistedRoles = new List<string>();

        sessions
            .Setup(store => store.AppendTurnAsync("session-1", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Callback<string, ConversationTurn, CancellationToken>((_, turn, _) => persistedRoles.Add(turn.Role))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.IsAny<ToolVisibilityContext>()))
            .Returns(Array.Empty<ToolDefinition>());

        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("continued output");

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig
            {
                LiteLlm = new LiteLlmConfig
                {
                    ContextWindowTokens = 128,
                    DefaultModel = "gpt-4o-mini"
                },
                Context = new ContextConfig()
            }),
            NullLogger<TurnPipeline>.Instance);

        var response = await pipeline.ProcessDetailedAsync(new LeanKernelMessage
        {
            Content = "Continue working on the task. Do not repeat completed steps; pick up where you left off.",
            SenderId = "user-1",
            ChannelId = "channel-1",
            SessionId = "session-1",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["turn_id"] = "root-turn-1",
                ["turnId"] = "root-turn-1",
                ["root_turn_id"] = "root-turn-1",
                ["rootTurnId"] = "root-turn-1",
                ["internal_turn"] = "true",
                ["internal_reason"] = "auto_continuation_prompt"
            }
        });

        response.Content.Should().Be("continued output");
        persistedRoles.Should().Equal("assistant");
        gatekeeper.VerifyAll();
        sessions.VerifyAll();
        strategy.VerifyAll();
        toolRegistry.VerifyAll();
    }

    [Fact]
    public async Task ProcessDetailedAsync_skips_persisting_legacy_auto_continuation_user_turn()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var persistedRoles = new List<string>();

        sessions
            .Setup(store => store.AppendTurnAsync("session-1", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Callback<string, ConversationTurn, CancellationToken>((_, turn, _) => persistedRoles.Add(turn.Role))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.IsAny<ToolVisibilityContext>()))
            .Returns(Array.Empty<ToolDefinition>());

        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("continued output");

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig
            {
                LiteLlm = new LiteLlmConfig
                {
                    ContextWindowTokens = 128,
                    DefaultModel = "gpt-4o-mini"
                },
                Context = new ContextConfig()
            }),
            NullLogger<TurnPipeline>.Instance);

        var response = await pipeline.ProcessDetailedAsync(new LeanKernelMessage
        {
            Content = "Continue working on the task. Do not repeat completed steps; pick up where you left off.",
            SenderId = "user-1",
            ChannelId = "channel-1",
            SessionId = "session-1",
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["turn_id"] = "root-turn-1",
                ["turnId"] = "root-turn-1",
                ["root_turn_id"] = "root-turn-1",
                ["rootTurnId"] = "root-turn-1",
                ["auto_continuation"] = "true"
            }
        });

        response.Content.Should().Be("continued output");
        persistedRoles.Should().Equal("assistant");
        gatekeeper.VerifyAll();
        sessions.VerifyAll();
        strategy.VerifyAll();
        toolRegistry.VerifyAll();
    }

    [Fact]
    public async Task ProcessAsync_persists_turns_merges_visible_tools_stores_context_snapshot_and_publishes_event()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var responseEnhancer = new Mock<IResponseEnhancer>(MockBehavior.Strict);
        var turnEventSink = new Mock<ITurnEventSink>(MockBehavior.Strict);
        var contextDiagnostics = new Mock<IContextDiagnosticsService>(MockBehavior.Strict);
        var diagnosticsSink = new Mock<IDiagnosticsSink>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "How is Atlas doing?",
            SenderId = "user-1",
            ChannelId = "channel-1",
            Timestamp = DateTimeOffset.Parse("2025-05-20T10:05:00Z")
        };

        var gatedContext = new ConversationContext
        {
            SystemPrompt = "Base policy",
            SessionId = "session-1",
            Identity = new IdentityContext
            {
                UserId = "user-1",
                PromptSegments = ["### User Preferences (identity-user-default)\n- preferred_name: Alex"]
            },
            Onboarding = new OnboardingResult
            {
                HasGaps = true,
                OnboardingDirective = "Continue answering the user's current request."
            },
            ActiveToolNames = ["wiki_read", "channel_tool"],
            BudgetUsage = new ContextBudgetUsage
            {
                SystemPromptUsed = 10,
                WikiFactsUsed = 8,
                RetrievalUsed = 6,
                ConversationUsed = 12,
                ToolsUsed = 0,
            },
            AdmissionLog =
            [
                new ContextAdmissionRecord { Key = "wiki-1", Source = "wiki", Score = 0.95, TokenCount = 4, Admitted = true },
                new ContextAdmissionRecord { Key = "doc-1", Source = "gbrain", Score = 0.05, TokenCount = 3, Admitted = false, ExclusionReason = "LowRelevanceScore" },
            ],
            HistoryDiagnostics = new HistoryShapingDiagnostics
            {
                TotalTurns = 3,
                VerbatimTurns = 1,
                CompactedTurns = 1,
                SummarizedTurns = 0,
                DroppedTurns = 1,
                TotalTokensBefore = 60,
                TotalTokensAfter = 35,
                BudgetAvailable = 40,
            },
            RetrievalDiagnostics = new RetrievalDiagnostics
            {
                SessionId = "unknown",
                TurnId = "unknown",
                TotalConsidered = 2,
                TotalAdmitted = 1,
                TotalExcludedByScope = 0,
                TotalExcludedByScore = 1,
                EffectiveScope = "personal",
            },
            RetrievedKnowledge =
            [
                new RetrievalCandidate { Key = "atlas-release", Content = "Atlas shipped with better diagnostics.", Source = "gbrain", Score = 0.92, TokenCount = 5 }
            ],
            History =
            [
                new ConversationTurn { Role = "assistant", Content = "Previous answer", Timestamp = DateTimeOffset.Parse("2025-05-20T10:04:00Z") }
            ]
        };

        ConversationTurn? persistedUserTurn = null;
        ConversationTurn? persistedAssistantTurn = null;
        AgentStrategyContext? capturedStrategyContext = null;
        ContextBudget? capturedBudget = null;
        LeanKernelMessage? capturedGatekeeperMessage = null;
        TurnEvent? publishedEvent = null;
        EnhancementStepInput? capturedEnhancementInput = null;
        EnhancementResult? capturedEnhancementResult = null;
        string? capturedDiagnosticsTurnId = null;
        ContextDiagnosticsSnapshot? capturedSnapshot = null;
        List<DiagnosticEntry> capturedDiagnostics = [];

        sessions
            .Setup(store => store.GetOrCreateSessionIdAsync("channel-1", "user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("session-1");
        sessions
            .Setup(store => store.AppendTurnAsync("session-1", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Callback<string, ConversationTurn, CancellationToken>((_, turn, _) =>
            {
                if (turn.Role == "user")
                {
                    persistedUserTurn = turn;
                }
                else
                {
                    persistedAssistantTurn = turn;
                }
            })
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-1", It.IsAny<CancellationToken>()))
            .Callback<LeanKernelMessage, ContextBudget, string, CancellationToken>((candidate, budget, _, _) =>
            {
                capturedGatekeeperMessage = candidate;
                capturedBudget = budget;
            })
            .ReturnsAsync(gatedContext);

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.Is<ToolVisibilityContext>(context => context.UserId == "user-1")))
            .Returns(
            [
                new ToolDefinition { Name = "wiki_search", Description = "Search wiki" },
                new ToolDefinition { Name = "WIKI_READ", Description = "Read wiki" }
            ]);

        contextDiagnostics
            .Setup(service => service.StoreContextDiagnosticsAsync("session-1", It.IsAny<string>(), It.IsAny<ContextDiagnosticsSnapshot>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, ContextDiagnosticsSnapshot, CancellationToken>((_, turnId, snapshot, _) =>
            {
                capturedDiagnosticsTurnId = turnId;
                capturedSnapshot = snapshot;
            })
            .Returns(Task.CompletedTask);

        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .Callback<AgentStrategyContext, CancellationToken>((context, _) =>
            {
                capturedStrategyContext = context;
                context.ModelUsed = "gpt-4o";
                context.RoutingDecision = new RoutingDecision
                {
                    SelectedTier = ModelTier.Standard,
                    SelectedModel = "gpt-4o",
                    ComplexityScore = 0.42,
                    Reason = "standard tier selected",
                    Factors = ["message-tokens:100:medium"],
                    EscalationAttempt = 0,
                };
                context.QualityGateResult = new QualityGateResult
                {
                    Outcome = QualityOutcome.Passed,
                    Passed = true,
                    OverallScore = 1.0,
                    Checks =
                    [
                        new QualityCheckResult
                        {
                            CheckName = "empty-response",
                            Passed = true,
                            Score = 1.0,
                        }
                    ]
                };
            })
            .ReturnsAsync("draft response");

        diagnosticsSink
            .Setup(sink => sink.RecordAsync(It.IsAny<DiagnosticEntry>(), It.IsAny<CancellationToken>()))
            .Callback<DiagnosticEntry, CancellationToken>((entry, _) => capturedDiagnostics.Add(entry))
            .Returns(Task.CompletedTask);

        responseEnhancer
            .Setup(enhancer => enhancer.EnhanceAsync(It.IsAny<EnhancementStepInput>(), It.IsAny<CancellationToken>()))
            .Callback<EnhancementStepInput, CancellationToken>((input, _) => capturedEnhancementInput = input)
            .ReturnsAsync(() => capturedEnhancementResult = new EnhancementResult
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
                        Duration = TimeSpan.FromMilliseconds(8)
                    }
                ],
                TotalDuration = TimeSpan.FromMilliseconds(8)
            });

        turnEventSink
            .Setup(sink => sink.PublishAsync(It.IsAny<TurnEvent>(), It.IsAny<CancellationToken>()))
            .Callback<TurnEvent, CancellationToken>((turnEvent, _) => publishedEvent = turnEvent)
            .Returns(Task.CompletedTask);

        var routingDiagnostics = new DiagnosticsCollector(
            NullLogger<DiagnosticsCollector>.Instance,
            Options.Create(new DiagnosticsConfig
            {
                Enabled = true,
                PersistToDatabase = true
            }),
            diagnosticsSink.Object);

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig
            {
                LiteLlm = new LiteLlmConfig
                {
                    ContextWindowTokens = 100,
                    DefaultModel = "gpt-4o-mini"
                },
                Context = new ContextConfig()
            }),
            NullLogger<TurnPipeline>.Instance,
            responseEnhancer.Object,
            turnEventSink.Object,
            contextDiagnostics.Object,
            routingDiagnostics);

        var response = await pipeline.ProcessAsync(message);

        response.Should().Be("enhanced response");
        capturedBudget.Should().NotBeNull();
        capturedBudget!.TotalTokens.Should().Be(75);
        capturedGatekeeperMessage.Should().NotBeNull();
        capturedGatekeeperMessage!.SessionId.Should().Be("session-1");
        capturedGatekeeperMessage.Metadata.Should().ContainKey("turn_id");
        capturedGatekeeperMessage.Metadata!["turn_id"].Should().Be(capturedGatekeeperMessage.Metadata["turnId"]);
        persistedUserTurn.Should().NotBeNull();
        persistedUserTurn!.Timestamp.Should().Be(message.Timestamp);
        persistedAssistantTurn.Should().NotBeNull();
        persistedAssistantTurn!.Content.Should().Be("enhanced response");
        capturedStrategyContext.Should().NotBeNull();
        capturedStrategyContext!.SessionId.Should().Be("session-1");
        capturedStrategyContext.TurnId.Should().Be(capturedDiagnosticsTurnId);
        capturedStrategyContext.UserMessage.Should().Be("How is Atlas doing?");
        capturedStrategyContext.History.Should().BeSameAs(gatedContext.History);
        capturedStrategyContext.Tools.Should().NotBeNull();
        capturedStrategyContext.Tools!.Should().HaveCount(2);
        capturedStrategyContext.Tools!.Select(tool => tool.Name).Should().Equal("wiki_search", "WIKI_READ");
        capturedStrategyContext.AvailableToolNames.Should().Equal("wiki_read", "channel_tool", "wiki_search");
        capturedStrategyContext.SystemMessage.Should().Contain("Base policy");
        capturedStrategyContext.SystemMessage.Should().Contain("## Identity Context");
        capturedStrategyContext.SystemMessage.Should().Contain("## Onboarding Guidance");
        capturedStrategyContext.SystemMessage.Should().Contain("## Available Tools: wiki_read, channel_tool, wiki_search");
        capturedEnhancementInput.Should().NotBeNull();
        capturedEnhancementInput!.Response.Should().Be("draft response");
        capturedEnhancementInput.UserMessage.Should().Be("How is Atlas doing?");
        capturedEnhancementInput.SessionId.Should().Be("session-1");
        capturedEnhancementInput.RetrievedKnowledge.Should().BeSameAs(gatedContext.RetrievedKnowledge);
        capturedSnapshot.Should().NotBeNull();
        capturedSnapshot!.Admissions.Should().BeSameAs(gatedContext.AdmissionLog);
        capturedSnapshot.Budget.Should().BeSameAs(capturedBudget);
        capturedSnapshot.BudgetUsage.Should().BeSameAs(gatedContext.BudgetUsage);
        capturedSnapshot.TotalBudgetTokens.Should().Be(100);
        capturedSnapshot.ResponseHeadroomRatio.Should().Be(0.25);
        capturedSnapshot.RetrievalDiagnostics.Should().NotBeNull();
        capturedSnapshot.RetrievalDiagnostics!.SessionId.Should().Be("session-1");
        capturedSnapshot.RetrievalDiagnostics.TurnId.Should().Be(capturedDiagnosticsTurnId);
        publishedEvent.Should().NotBeNull();
        publishedEvent!.SessionId.Should().Be("session-1");
        publishedEvent.TurnId.Should().Be(capturedDiagnosticsTurnId);
        publishedEvent.Content.Should().Be("enhanced response");
        publishedEvent.UserMessage.Should().Be("How is Atlas doing?");
        publishedEvent.AssistantResponse.Should().Be("enhanced response");
        publishedEvent.ModelUsed.Should().Be("gpt-4o");
        publishedEvent.RoutingDecision.Should().NotBeNull();
        publishedEvent.RoutingDecision!.SelectedTier.Should().Be(ModelTier.Standard);
        publishedEvent.RoutingDecision.SelectedModel.Should().Be("gpt-4o");
        capturedDiagnostics.Should().Contain(entry =>
            entry.Category == DiagnosticCategory.ModelRouting.ToString()
            && ReferenceEquals(entry.Payload, capturedStrategyContext.RoutingDecision));
        capturedDiagnostics.Should().Contain(entry =>
            entry.Category == DiagnosticCategory.QualityGate.ToString()
            && ReferenceEquals(entry.Payload, capturedStrategyContext.QualityGateResult));
        capturedDiagnostics.Should().Contain(entry =>
            entry.Category == DiagnosticCategory.ResponseEnhancement.ToString()
            && ReferenceEquals(entry.Payload, capturedEnhancementResult));
        publishedEvent.Context.Should().NotBeNull();
        publishedEvent.Context!.ActiveToolNames.Should().Equal("wiki_read", "channel_tool", "wiki_search");
        publishedEvent.Context.Identity.Should().BeSameAs(gatedContext.Identity);
        publishedEvent.Context.Onboarding.Should().BeSameAs(gatedContext.Onboarding);

        sessions.VerifyAll();
        gatekeeper.VerifyAll();
        toolRegistry.VerifyAll();
        contextDiagnostics.VerifyAll();
        strategy.VerifyAll();
        responseEnhancer.VerifyAll();
        turnEventSink.VerifyAll();
        diagnosticsSink.VerifyAll();
    }

    [Fact]
    public async Task ProcessAsync_logs_and_continues_when_diagnostics_and_event_publication_fail()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var contextDiagnostics = new Mock<IContextDiagnosticsService>(MockBehavior.Strict);
        var turnEventSink = new Mock<ITurnEventSink>(MockBehavior.Strict);
        var diagnosticsSink = new Mock<IDiagnosticsSink>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "Investigate Atlas",
            SenderId = "user-1",
            ChannelId = "channel-1"
        };

        sessions
            .Setup(store => store.GetOrCreateSessionIdAsync("channel-1", "user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("session-1");
        sessions
            .Setup(store => store.AppendTurnAsync("session-1", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(item => item.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(item => item.GetVisibleTools(It.IsAny<ToolVisibilityContext>()))
            .Returns(Array.Empty<ToolDefinition>());

        contextDiagnostics
            .Setup(item => item.StoreContextDiagnosticsAsync("session-1", It.IsAny<string>(), It.IsAny<ContextDiagnosticsSnapshot>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("context diagnostics failed"));

        strategy
            .Setup(item => item.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .Callback<AgentStrategyContext, CancellationToken>((context, _) =>
            {
                context.ModelUsed = "gpt-4o";
                context.RoutingDecision = new RoutingDecision
                {
                    SelectedTier = ModelTier.Standard,
                    SelectedModel = "gpt-4o",
                    ComplexityScore = 0.5,
                    Reason = "standard tier selected",
                    Factors = ["tooling:0"],
                    EscalationAttempt = 0,
                };
                context.QualityGateResult = new QualityGateResult
                {
                    Outcome = QualityOutcome.Passed,
                    Passed = true,
                    OverallScore = 1.0,
                    Checks = []
                };
                context.OrchestrationResult = new OrchestrationResult
                {
                    CoordinatorResponse = "response",
                    TotalDuration = TimeSpan.FromSeconds(1),
                    TotalWorkerInvocations = 0
                };
            })
            .ReturnsAsync("response");

        diagnosticsSink
            .Setup(item => item.RecordAsync(It.IsAny<DiagnosticEntry>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("diagnostics failed"));

        turnEventSink
            .Setup(item => item.PublishAsync(It.IsAny<TurnEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("event publication failed"));

        var diagnosticsCollector = new DiagnosticsCollector(
            NullLogger<DiagnosticsCollector>.Instance,
            Options.Create(new DiagnosticsConfig
            {
                Enabled = true,
                PersistToDatabase = true
            }),
            diagnosticsSink.Object);

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig()),
            NullLogger<TurnPipeline>.Instance,
            turnEventSink: turnEventSink.Object,
            contextDiagnosticsService: contextDiagnostics.Object,
            diagnosticsCollector: diagnosticsCollector);

        var response = await pipeline.ProcessAsync(message);

        response.Should().Be("response");
        sessions.Verify(store => store.AppendTurnAsync("session-1", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        strategy.VerifyAll();
        contextDiagnostics.VerifyAll();
        diagnosticsSink.Verify(item => item.RecordAsync(It.IsAny<DiagnosticEntry>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        turnEventSink.VerifyAll();
    }

    [Fact]
    public async Task ProcessAsync_uses_existing_session_id_without_lookup_and_skips_optional_collaborators()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "Status?",
            SenderId = "user-2",
            ChannelId = "channel-1",
            SessionId = "existing-session"
        };

        sessions
            .Setup(store => store.AppendTurnAsync("existing-session", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(
                It.Is<LeanKernelMessage>(candidate =>
                    candidate.Content == "Status?"
                    && candidate.SessionId == "existing-session"
                    && candidate.Metadata != null
                    && candidate.Metadata.ContainsKey("turn_id")),
                It.IsAny<ContextBudget>(),
                "existing-session",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.Is<ToolVisibilityContext>(context => context.UserId == "user-2")))
            .Returns(Array.Empty<ToolDefinition>());

        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("response");

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig()),
            NullLogger<TurnPipeline>.Instance);

        var response = await pipeline.ProcessAsync(message);

        response.Should().Be("response");
        sessions.Verify(store => store.GetOrCreateSessionIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        sessions.VerifyAll();
        gatekeeper.VerifyAll();
        toolRegistry.VerifyAll();
        strategy.VerifyAll();
    }

    [Fact]
    public async Task ProcessDetailedAsync_surfaces_attachment_metadata_in_prompt_and_parses_signal_attachment_directive()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "Please send this back",
            SenderId = "user-3",
            ChannelId = "signal",
            Attachments =
            [
                new Attachment
                {
                    FileName = "invoice.pdf",
                    ContentType = "application/pdf",
                    Data = Array.Empty<byte>(),
                }
            ]
        };

        sessions
            .Setup(store => store.GetOrCreateSessionIdAsync("signal", "user-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync("session-3");
        sessions
            .Setup(store => store.AppendTurnAsync("session-3", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.Is<ToolVisibilityContext>(context => context.UserId == "user-3")))
            .Returns(Array.Empty<ToolDefinition>());

        AgentStrategyContext? capturedStrategyContext = null;
        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .Callback<AgentStrategyContext, CancellationToken>((context, _) => capturedStrategyContext = context)
            .ReturnsAsync("Here you go.\n```signal-attachments\n{\"attachments\":[{\"source\":\"incoming\",\"index\":1}]}\n```");

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig()),
            NullLogger<TurnPipeline>.Instance);

        var response = await pipeline.ProcessDetailedAsync(message);

        capturedStrategyContext.Should().NotBeNull();
        capturedStrategyContext!.UserMessage.Should().Contain("## Incoming Attachments");
        capturedStrategyContext.UserMessage.Should().Contain("invoice.pdf");
        capturedStrategyContext.UserMessage.Should().Contain("```signal-attachments");

        response.Content.Should().Be("Here you go.");
        response.Attachments.Should().NotBeNull();
        response.Attachments!.Should().ContainSingle();
        response.Attachments[0].FileName.Should().Be("invoice.pdf");
        response.Attachments[0].Data.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessDetailedAsync_parses_task_status_directives_into_execution_metadata()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "Please continue",
            SenderId = "user-task-status",
            ChannelId = "signal",
            SessionId = "session-task-status"
        };

        sessions
            .Setup(store => store.AppendTurnAsync("session-task-status", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-task-status", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.IsAny<ToolVisibilityContext>()))
            .Returns(Array.Empty<ToolDefinition>());

        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Drafting the final answer.\n```task-status\n{\"status\":\"in_progress\",\"note\":\"Still refining the answer.\"}\n```");

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig()),
            NullLogger<TurnPipeline>.Instance);

        var response = await pipeline.ProcessDetailedAsync(message);

        response.Content.Should().Be("Drafting the final answer.");
        response.Execution.Should().NotBeNull();
        response.Execution!.ToolInvocationCount.Should().Be(0);
        response.Execution.TaskStatus.Should().NotBeNull();
        response.Execution.TaskStatus!.Status.Should().Be("in_progress");
        response.Execution.TaskStatus.Note.Should().Be("Still refining the answer.");
    }

    [Fact]
    public async Task ProcessDetailedAsync_ignores_malformed_task_status_directives()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "Please continue",
            SenderId = "user-task-status-malformed",
            ChannelId = "signal",
            SessionId = "session-task-status-malformed"
        };

        sessions
            .Setup(store => store.AppendTurnAsync("session-task-status-malformed", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-task-status-malformed", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.Is<ToolVisibilityContext>(context => context.UserId == "user-task-status-malformed")))
            .Returns(Array.Empty<ToolDefinition>());

        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Work in progress.\n```task-status\n{\"status\":\n```");

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig()),
            NullLogger<TurnPipeline>.Instance);

        var response = await pipeline.ProcessDetailedAsync(message);

        response.Content.Should().Contain("Work in progress.");
        response.Execution.Should().NotBeNull();
        response.Execution!.TaskStatus.Should().BeNull();

        sessions.VerifyAll();
        gatekeeper.VerifyAll();
        toolRegistry.VerifyAll();
        strategy.VerifyAll();
    }

    [Fact]
    public async Task ProcessDetailedAsync_ignores_signal_attachment_directives_when_no_incoming_attachments_are_present()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "Please send this back",
            SenderId = "user-attachment-none",
            ChannelId = "signal",
            SessionId = "session-attachment-none"
        };

        sessions
            .Setup(store => store.AppendTurnAsync("session-attachment-none", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-attachment-none", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.Is<ToolVisibilityContext>(context => context.UserId == "user-attachment-none")))
            .Returns(Array.Empty<ToolDefinition>());

        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Here you go.\n```signal-attachments\n{\"attachments\":[{\"source\":\"incoming\",\"index\":1}]}\n```");

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig()),
            NullLogger<TurnPipeline>.Instance);

        var response = await pipeline.ProcessDetailedAsync(message);

        response.Content.Should().Be("Here you go.");
        response.Attachments.Should().BeNull();

        sessions.VerifyAll();
        gatekeeper.VerifyAll();
        toolRegistry.VerifyAll();
        strategy.VerifyAll();
    }

    [Fact]
    public async Task ProcessDetailedAsync_returns_empty_content_when_the_model_returns_only_whitespace()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "Status?",
            SenderId = "user-empty-response",
            ChannelId = "signal",
            SessionId = "session-empty-response"
        };

        sessions
            .Setup(store => store.AppendTurnAsync("session-empty-response", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-empty-response", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.Is<ToolVisibilityContext>(context => context.UserId == "user-empty-response")))
            .Returns(Array.Empty<ToolDefinition>());

        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig()),
            NullLogger<TurnPipeline>.Instance);

        var response = await pipeline.ProcessDetailedAsync(message);

        response.Content.Should().BeEmpty();
        response.Execution.Should().NotBeNull();
        response.Execution!.ToolInvocationCount.Should().Be(0);

        sessions.VerifyAll();
        gatekeeper.VerifyAll();
        toolRegistry.VerifyAll();
        strategy.VerifyAll();
    }

    [Fact]
    public async Task ProcessDetailedAsync_filters_browser_tools_selects_core_tools_and_redacts_sensitive_arguments()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var toolSelector = new Mock<IToolSelector>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "Use the browser tool and summarize the result",
            SenderId = "user-tooling",
            ChannelId = "signal",
            SessionId = "session-tooling"
        };

        sessions
            .Setup(store => store.AppendTurnAsync("session-tooling", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-tooling", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        var browserRunTask = new ToolDefinition
        {
            Name = "browser_run_task",
            Description = "Run browser task",
            Handler = (_, _) => Task.FromResult(new ToolResult
            {
                ToolName = "browser_run_task",
                Success = true,
                Output = "browser ok"
            })
        };

        var toolDefinitions = new[]
        {
            browserRunTask,
            new ToolDefinition
            {
                Name = "browser_get_run",
                Description = "Get browser run",
                Handler = (_, _) => Task.FromResult(new ToolResult { ToolName = "browser_get_run", Success = true, Output = "run ok" })
            },
            new ToolDefinition
            {
                Name = "browser_get_artifact",
                Description = "Get browser artifact",
                Handler = (_, _) => Task.FromResult(new ToolResult { ToolName = "browser_get_artifact", Success = true, Output = "artifact ok" })
            },
            new ToolDefinition
            {
                Name = "browser_cancel_run",
                Description = "Cancel browser run",
                Handler = (_, _) => Task.FromResult(new ToolResult { ToolName = "browser_cancel_run", Success = true, Output = "cancel ok" })
            },
            new ToolDefinition
            {
                Name = "web_actions_legacy",
                Description = "Legacy browser wrapper",
                Handler = (_, _) => Task.FromResult(new ToolResult { ToolName = "web_actions_legacy", Success = true, Output = "legacy ok" })
            },
            new ToolDefinition
            {
                Name = "ms-todo-plan",
                Description = "List todo items",
                Handler = (_, _) => Task.FromResult(new ToolResult { ToolName = "ms-todo-plan", Success = true, Output = "todo ok" })
            },
            new ToolDefinition
            {
                Name = "wiki_read",
                Description = "Read wiki",
                Handler = (_, _) => Task.FromResult(new ToolResult { ToolName = "wiki_read", Success = true, Output = "wiki ok" })
            }
        };

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.Is<ToolVisibilityContext>(context => context.UserId == "user-tooling")))
            .Returns(toolDefinitions);

        toolSelector
            .Setup(selector => selector.SelectToolsAsync(
                It.IsAny<string>(),
                It.Is<IReadOnlyList<ToolDefinition>>(tools => tools.Count == 6 && tools.All(tool => tool.Name != "web_actions_legacy")),
                2,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([browserRunTask]);

        AgentStrategyContext? capturedStrategyContext = null;
        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .Callback<AgentStrategyContext, CancellationToken>((context, _) => capturedStrategyContext = context)
            .Returns<AgentStrategyContext, CancellationToken>(async (context, ct) =>
            {
                var function = context.Tools!.First().GetService<AIFunction>();
                function.Should().NotBeNull();
                if (function is not null)
                {
                    await function.InvokeAsync(new AIFunctionArguments(
                        new Dictionary<string, object?>
                        {
                            ["token"] = "super-secret-token",
                            ["query"] = "browser query"
                        }), ct).ConfigureAwait(false);
                }

                context.ModelUsed = "gpt-4o";
                return "tooling complete";
            });

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig
            {
                LiteLlm = new LiteLlmConfig
                {
                    ContextWindowTokens = 100,
                    DefaultModel = "gpt-4o-mini",
                    MaxTools = 2
                }
            }),
            NullLogger<TurnPipeline>.Instance,
            toolSelector: toolSelector.Object);

        var response = await pipeline.ProcessDetailedAsync(message);

        response.Content.Should().Be("tooling complete");
        capturedStrategyContext.Should().NotBeNull();
        capturedStrategyContext!.Tools.Should().NotBeNull();
        capturedStrategyContext.Tools!.Select(tool => tool.Name).Should().Contain("browser_run_task");
        capturedStrategyContext.Tools!.Select(tool => tool.Name).Should().Contain("browser_get_run");
        capturedStrategyContext.Tools!.Select(tool => tool.Name).Should().NotContain("web_actions_legacy");

        sessions.VerifyAll();
        gatekeeper.VerifyAll();
        toolRegistry.VerifyAll();
        toolSelector.VerifyAll();
        strategy.VerifyAll();
    }

    [Fact]
    public async Task ProcessDetailedAsync_returns_degraded_response_when_model_invocation_fails()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var providerHealthTracker = new Mock<IProviderHealthTracker>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "Explain the rollout",
            SenderId = "user-model-failure",
            ChannelId = "signal",
            SessionId = "session-model-failure"
        };

        sessions
            .Setup(store => store.AppendTurnAsync("session-model-failure", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-model-failure", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.Is<ToolVisibilityContext>(context => context.UserId == "user-model-failure")))
            .Returns(Array.Empty<ToolDefinition>());

        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("model failed"));

        providerHealthTracker
            .Setup(tracker => tracker.RecordProbeResult(
                ProviderNames.LiteLlm,
                It.Is<ProviderProbeResult>(result =>
                    !result.IsHealthy
                    && result.Description == "Model invocation failed."
                    && result.ErrorMessage == "model failed")));

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig
            {
                LiteLlm = new LiteLlmConfig
                {
                    ContextWindowTokens = 100,
                    DefaultModel = "gpt-4o-mini"
                }
            }),
            NullLogger<TurnPipeline>.Instance,
            providerHealthTracker: providerHealthTracker.Object);

        var response = await pipeline.ProcessDetailedAsync(message);

        response.Content.Should().Be("LeanKernel cannot reach the configured model provider right now. Please try again shortly.");
        response.Execution.Should().NotBeNull();
        response.Execution!.ToolInvocationCount.Should().Be(0);
        response.Execution.SuccessfulToolInvocations.Should().Be(0);
        providerHealthTracker.VerifyAll();
        sessions.VerifyAll();
        gatekeeper.VerifyAll();
        toolRegistry.VerifyAll();
        strategy.VerifyAll();
    }

    [Fact]
    public async Task ProcessDetailedAsync_returns_the_spend_guard_reason_without_invoking_the_model_when_blocked()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var spendGuardService = new Mock<ISpendGuardService>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "Explain the rollout",
            SenderId = "user-spend-block",
            ChannelId = "signal",
            SessionId = "session-spend-block"
        };

        sessions
            .Setup(store => store.AppendTurnAsync("session-spend-block", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-spend-block", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.Is<ToolVisibilityContext>(context => context.UserId == "user-spend-block")))
            .Returns(Array.Empty<ToolDefinition>());

        spendGuardService
            .Setup(service => service.Evaluate("session-spend-block", ModelTier.Standard, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTimeOffset?>()))
            .Returns(new SpendGuardDecision
            {
                Action = SpendGuardAction.Block,
                Reason = "Budget exhausted",
                DailyLimitUsd = 10,
                DailySpendUsd = 10,
            });

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig
            {
                LiteLlm = new LiteLlmConfig
                {
                    ContextWindowTokens = 100,
                    DefaultModel = "gpt-4o-mini"
                }
            }),
            NullLogger<TurnPipeline>.Instance,
            spendGuardService: spendGuardService.Object);

        var response = await pipeline.ProcessDetailedAsync(message);

        response.Content.Should().Be("Budget exhausted");
        response.Execution.Should().NotBeNull();
        response.Execution!.ToolInvocationCount.Should().Be(0);
        response.Execution.SuccessfulToolInvocations.Should().Be(0);
        strategy.Verify(candidate => candidate.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()), Times.Never);
        sessions.VerifyAll();
        gatekeeper.VerifyAll();
        toolRegistry.VerifyAll();
        spendGuardService.VerifyAll();
    }

    [Fact]
    public async Task ProcessDetailedAsync_keeps_the_original_response_when_enhancement_fails()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var responseEnhancer = new Mock<IResponseEnhancer>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "Explain the rollout",
            SenderId = "user-enhancement-failure",
            ChannelId = "signal",
            SessionId = "session-enhancement-failure"
        };

        sessions
            .Setup(store => store.AppendTurnAsync("session-enhancement-failure", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-enhancement-failure", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.Is<ToolVisibilityContext>(context => context.UserId == "user-enhancement-failure")))
            .Returns(Array.Empty<ToolDefinition>());

        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("draft response");

        responseEnhancer
            .Setup(enhancer => enhancer.EnhanceAsync(It.IsAny<EnhancementStepInput>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("enhancement failed"));

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig
            {
                LiteLlm = new LiteLlmConfig
                {
                    ContextWindowTokens = 100,
                    DefaultModel = "gpt-4o-mini"
                }
            }),
            NullLogger<TurnPipeline>.Instance,
            responseEnhancer: responseEnhancer.Object);

        var response = await pipeline.ProcessDetailedAsync(message);

        response.Content.Should().Be("draft response");
        response.Execution.Should().NotBeNull();
        response.Execution!.ToolInvocationCount.Should().Be(0);
        response.Execution.SuccessfulToolInvocations.Should().Be(0);

        sessions.VerifyAll();
        gatekeeper.VerifyAll();
        toolRegistry.VerifyAll();
        strategy.VerifyAll();
        responseEnhancer.VerifyAll();
    }

    [Fact]
    public async Task ProcessDetailedAsync_appends_system_notices_and_records_failed_tool_invocations_when_a_tool_throws()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);
        var degradationPolicy = new Mock<IGracefulDegradationPolicy>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "Review the latest plan",
            SenderId = "user-tool-failure",
            ChannelId = "signal",
            SessionId = "session-tool-failure"
        };

        sessions
            .Setup(store => store.AppendTurnAsync("session-tool-failure", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-tool-failure", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.Is<ToolVisibilityContext>(context => context.UserId == "user-tool-failure")))
            .Returns(
            [
                new ToolDefinition
                {
                    Name = "flaky_tool",
                    Description = "Fails when invoked",
                    Handler = (_, _) => Task.FromException<ToolResult>(new InvalidOperationException("boom"))
                }
            ]);

        degradationPolicy
            .Setup(policy => policy.Evaluate())
            .Returns(new GracefulDegradationDecision
            {
                Warnings = ["Model capacity is reduced", "Model capacity is reduced"]
            });

        AgentStrategyContext? capturedStrategyContext = null;
        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .Returns<AgentStrategyContext, CancellationToken>(async (context, ct) =>
            {
                capturedStrategyContext = context;
                var function = context.Tools!.Single().GetService<AIFunction>();
                function.Should().NotBeNull();

                if (function is not null)
                {
                    try
                    {
                        await function.InvokeAsync(
                            new AIFunctionArguments(new Dictionary<string, object?>()),
                            ct).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }

                context.ModelUsed = "gpt-4o";
                return "Draft response";
            });

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig
            {
                LiteLlm = new LiteLlmConfig
                {
                    ContextWindowTokens = 100,
                    DefaultModel = "gpt-4o-mini"
                }
            }),
            NullLogger<TurnPipeline>.Instance,
            gracefulDegradationPolicy: degradationPolicy.Object);

        var response = await pipeline.ProcessDetailedAsync(message);

        response.Content.Should().Be(
            $"Draft response{Environment.NewLine}{Environment.NewLine}System notices:{Environment.NewLine}- Model capacity is reduced");
        response.Execution.Should().NotBeNull();
        response.Execution!.ToolInvocationCount.Should().Be(1);
        response.Execution.SuccessfulToolInvocations.Should().Be(0);
        response.Execution.TaskStatus.Should().BeNull();
        response.Execution.ModelUsed.Should().Be("gpt-4o");
        capturedStrategyContext.Should().NotBeNull();
        capturedStrategyContext!.Tools.Should().HaveCount(1);

        sessions.VerifyAll();
        gatekeeper.VerifyAll();
        toolRegistry.VerifyAll();
        degradationPolicy.VerifyAll();
        strategy.VerifyAll();
    }

    [Fact]
    public async Task ProcessDetailedAsync_records_cancelled_tool_invocations_and_recovers()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "Continue the investigation",
            SenderId = "user-tool-cancel",
            ChannelId = "signal",
            SessionId = "session-tool-cancel"
        };

        sessions
            .Setup(store => store.AppendTurnAsync("session-tool-cancel", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-tool-cancel", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.Is<ToolVisibilityContext>(context => context.UserId == "user-tool-cancel")))
            .Returns(
            [
                new ToolDefinition
                {
                    Name = "cancelled_tool",
                    Description = "Cancels when invoked",
                    Handler = (_, _) => Task.FromException<ToolResult>(new OperationCanceledException("cancelled"))
                }
            ]);

        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .Returns<AgentStrategyContext, CancellationToken>(async (context, ct) =>
            {
                var function = context.Tools!.Single().GetService<AIFunction>();
                function.Should().NotBeNull();

                if (function is not null)
                {
                    try
                    {
                        await function.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>()), ct)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }

                context.ModelUsed = "gpt-4o";
                return "Recovered response";
            });

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig
            {
                LiteLlm = new LiteLlmConfig
                {
                    ContextWindowTokens = 100,
                    DefaultModel = "gpt-4o-mini"
                }
            }),
            NullLogger<TurnPipeline>.Instance);

        var response = await pipeline.ProcessDetailedAsync(message);

        response.Content.Should().Be("Recovered response");
        response.Execution.Should().NotBeNull();
        response.Execution!.ToolInvocationCount.Should().Be(1);
        response.Execution.SuccessfulToolInvocations.Should().Be(0);
        response.Execution.TaskStatus.Should().BeNull();

        sessions.VerifyAll();
        gatekeeper.VerifyAll();
        toolRegistry.VerifyAll();
        strategy.VerifyAll();
    }

    [Fact]
    public async Task ProcessDetailedAsync_ignores_invalid_task_status_directives()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "Please continue",
            SenderId = "user-task-status-invalid",
            ChannelId = "signal",
            SessionId = "session-task-status-invalid"
        };

        sessions
            .Setup(store => store.AppendTurnAsync("session-task-status-invalid", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-task-status-invalid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.Is<ToolVisibilityContext>(context => context.UserId == "user-task-status-invalid")))
            .Returns(Array.Empty<ToolDefinition>());

        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Work in progress.\n```task-status\n{\"status\":\"maybe\",\"note\":\"Nope\"}\n```");

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig
            {
                LiteLlm = new LiteLlmConfig
                {
                    ContextWindowTokens = 100,
                    DefaultModel = "gpt-4o-mini"
                }
            }),
            NullLogger<TurnPipeline>.Instance);

        var response = await pipeline.ProcessDetailedAsync(message);

        response.Content.Should().Be("Work in progress.");
        response.Execution.Should().NotBeNull();
        response.Execution!.ToolInvocationCount.Should().Be(0);
        response.Execution.SuccessfulToolInvocations.Should().Be(0);
        response.Execution.TaskStatus.Should().BeNull();

        sessions.VerifyAll();
        gatekeeper.VerifyAll();
        toolRegistry.VerifyAll();
        strategy.VerifyAll();
    }
}

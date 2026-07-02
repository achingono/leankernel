using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents;
using LeanKernel.Agents.Strategies;
using LeanKernel.Context;
using LeanKernel.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Agents;

public class TurnPipelineTests
{
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

        var attachmentData = new byte[] { 1, 2, 3, 4 };
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
                    Data = attachmentData,
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
        response.Attachments[0].Data.Should().Equal(attachmentData);
    }

    [Fact]
    public async Task ProcessDetailedAsync_includes_extracted_attachment_text_in_user_message()
    {
        var gatekeeper = new Mock<IContextGatekeeper>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var strategy = new Mock<IAgentStrategy>(MockBehavior.Strict);
        var toolRegistry = new Mock<IToolRegistry>(MockBehavior.Strict);

        var message = new LeanKernelMessage
        {
            Content = "Read attached note",
            SenderId = "user-attachment-text",
            ChannelId = "signal",
            Attachments =
            [
                new Attachment
                {
                    FileName = "note.txt",
                    ContentType = "text/plain",
                    Data = "line one\nline two"u8.ToArray(),
                }
            ]
        };

        sessions
            .Setup(store => store.GetOrCreateSessionIdAsync("signal", "user-attachment-text", It.IsAny<CancellationToken>()))
            .ReturnsAsync("session-attachment-text");
        sessions
            .Setup(store => store.AppendTurnAsync("session-attachment-text", It.IsAny<ConversationTurn>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        gatekeeper
            .Setup(g => g.GateContextAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<ContextBudget>(), "session-attachment-text", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationContext { SystemPrompt = "Base policy" });

        toolRegistry
            .Setup(registry => registry.GetVisibleTools(It.IsAny<ToolVisibilityContext>()))
            .Returns(Array.Empty<ToolDefinition>());

        AgentStrategyContext? capturedStrategyContext = null;
        strategy
            .Setup(s => s.InvokeAsync(It.IsAny<AgentStrategyContext>(), It.IsAny<CancellationToken>()))
            .Callback<AgentStrategyContext, CancellationToken>((context, _) => capturedStrategyContext = context)
            .ReturnsAsync("ack");

        var pipeline = new TurnPipeline(
            gatekeeper.Object,
            sessions.Object,
            strategy.Object,
            new PromptAssembler(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance),
            toolRegistry.Object,
            Options.Create(new LeanKernelConfig
            {
                FileSystem = new FileSystemConfig
                {
                    ScratchRoot = Path.GetTempPath(),
                    MaxExtractedCharacters = 500
                }
            }),
            NullLogger<TurnPipeline>.Instance);

        var response = await pipeline.ProcessDetailedAsync(message);

        response.Content.Should().Be("ack");
        capturedStrategyContext.Should().NotBeNull();
        capturedStrategyContext!.UserMessage.Should().Contain("Extracted content:");
        capturedStrategyContext.UserMessage.Should().Contain("line one");
        capturedStrategyContext.UserMessage.Should().Contain("line two");
    }
}

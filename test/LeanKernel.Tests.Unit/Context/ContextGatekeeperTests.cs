using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context;
using LeanKernel.Context.Identity;
using LeanKernel.Context.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Context;

public class ContextGatekeeperTests
{
    [Fact]
    public async Task GateContextAsync_returns_deny_by_default_context_when_no_candidates_are_available()
    {
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);

        knowledge
            .Setup(service => service.SearchAsync("Need status", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RetrievalCandidate>());
        sessions
            .Setup(store => store.GetHistoryAsync("session-1", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ConversationTurn>());

        var gatekeeper = CreateGatekeeper(knowledge.Object, sessions.Object);

        var context = await gatekeeper.GateContextAsync(
            CreateMessage("Need status"),
            CreateBudget(totalTokens: 20, conversationBudget: 5, knowledgeBudget: 5),
            "session-1");

        context.SystemPrompt.Should().Contain("You are LeanKernel");
        context.WikiFacts.Should().BeEmpty();
        context.RetrievedKnowledge.Should().BeEmpty();
        context.History.Should().BeEmpty();
        context.Identity.Should().NotBeNull();
        context.Onboarding.Should().NotBeNull();
        context.AdmissionLog.Should().BeEmpty();
        context.BudgetUsage.Should().NotBeNull();
        context.BudgetUsage!.SystemPromptUsed.Should().BeGreaterThan(0);
        context.BudgetUsage.TotalUsed.Should().Be(context.BudgetUsage.SystemPromptUsed);

        knowledge.VerifyAll();
        sessions.VerifyAll();
    }

    [Fact]
    public async Task GateContextAsync_admits_high_score_candidates_and_rejects_low_score_candidates()
    {
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);

        knowledge
            .Setup(service => service.SearchAsync("Need status", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RetrievalCandidate { Key = "wiki-1", Content = "Atlas owner", Source = "wiki", Score = 0.95, TokenCount = 1 },
                new RetrievalCandidate { Key = "doc-1", Content = "Low confidence", Source = "gbrain", Score = 0.05, TokenCount = 1 },
            });
        sessions
            .Setup(store => store.GetHistoryAsync("session-1", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ConversationTurn>());

        var gatekeeper = CreateGatekeeper(knowledge.Object, sessions.Object);

        var context = await gatekeeper.GateContextAsync(
            CreateMessage("Need status"),
            CreateBudget(totalTokens: 20, conversationBudget: 5, knowledgeBudget: 5),
            "session-1");

        context.WikiFacts.Select(candidate => candidate.Key).Should().Equal("wiki-1");
        context.RetrievedKnowledge.Should().BeEmpty();
        context.AdmissionLog.Select(record => (record.Key, record.Admitted, record.ExclusionReason)).Should().Equal(
            ("wiki-1", true, (string?)null),
            ("doc-1", false, "LowRelevanceScore"));

        knowledge.VerifyAll();
        sessions.VerifyAll();
    }

    [Fact]
    public async Task GateContextAsync_stops_admission_when_shared_knowledge_budget_is_exhausted()
    {
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);

        knowledge
            .Setup(service => service.SearchAsync("Need status", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RetrievalCandidate { Key = "wiki-1", Content = "12345678", Source = "wiki", Score = 0.9, TokenCount = 2 },
                new RetrievalCandidate { Key = "doc-1", Content = "1234", Source = "gbrain", Score = 0.8, TokenCount = 0 },
                new RetrievalCandidate { Key = "doc-2", Content = "12345678", Source = "gbrain", Score = 0.7, TokenCount = 2 },
            });
        sessions
            .Setup(store => store.GetHistoryAsync("session-1", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ConversationTurn>());

        var gatekeeper = CreateGatekeeper(knowledge.Object, sessions.Object);

        var context = await gatekeeper.GateContextAsync(
            CreateMessage("Need status"),
            CreateBudget(totalTokens: 20, conversationBudget: 5, knowledgeBudget: 3),
            "session-1");

        context.WikiFacts.Select(candidate => candidate.Key).Should().Equal("wiki-1");
        context.RetrievedKnowledge.Select(candidate => candidate.Key).Should().Equal("doc-1");
        context.RetrievedKnowledge[0].TokenCount.Should().Be(1);
        context.AdmissionLog.Select(record => (record.Key, record.Admitted, record.ExclusionReason)).Should().Equal(
            ("wiki-1", true, (string?)null),
            ("doc-1", true, (string?)null),
            ("doc-2", false, "BudgetExhausted"));
        context.BudgetUsage!.WikiFactsUsed.Should().Be(2);
        context.BudgetUsage.RetrievalUsed.Should().Be(1);

        knowledge.VerifyAll();
        sessions.VerifyAll();
    }

    [Fact]
    public async Task GateContextAsync_limits_history_to_the_conversation_budget()
    {
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);

        knowledge
            .Setup(service => service.SearchAsync("Need status", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RetrievalCandidate>());
        sessions
            .Setup(store => store.GetHistoryAsync("session-1", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new ConversationTurn { Role = "user", Content = "1234" },
                new ConversationTurn { Role = "assistant", Content = "5678" },
                new ConversationTurn { Role = "user", Content = "90ab" },
            });

        var gatekeeper = CreateGatekeeper(knowledge.Object, sessions.Object);

        var context = await gatekeeper.GateContextAsync(
            CreateMessage("Need status"),
            CreateBudget(totalTokens: 20, conversationBudget: 2, knowledgeBudget: 5),
            "session-1");

        context.History.Select(turn => turn.Content).Should().Equal("5678", "90ab");
        context.BudgetUsage!.ConversationUsed.Should().Be(2);

        knowledge.VerifyAll();
        sessions.VerifyAll();
    }

    [Fact]
    public async Task GateContextAsync_loads_identity_before_retrieval_and_adds_onboarding_directive()
    {
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var identityProvider = new Mock<IIdentityProvider>(MockBehavior.Strict);
        var onboardingDetector = new Mock<IOnboardingDetector>(MockBehavior.Strict);
        var sequence = new List<string>();

        identityProvider
            .Setup(provider => provider.LoadIdentityAsync("user-1", It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("identity"))
            .ReturnsAsync(new IdentityContext
            {
                UserId = "user-1",
                PromptSegments = ["### User Preferences (identity-user-default)\n- preferred_name: Alex"],
                OverallConfidence = 0.4,
            });
        onboardingDetector
            .Setup(detector => detector.DetectGapsAsync(It.IsAny<IdentityContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("onboarding"))
            .ReturnsAsync(new OnboardingResult
            {
                HasGaps = true,
                Gaps =
                [
                    new IdentityGap { FieldName = "timezone", GapCode = "missing_timezone" }
                ],
            });
        knowledge
            .Setup(service => service.SearchAsync("Need status", 20, It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("search"))
            .ReturnsAsync(Array.Empty<RetrievalCandidate>());
        sessions
            .Setup(store => store.GetHistoryAsync("session-1", 50, It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("history"))
            .ReturnsAsync(Array.Empty<ConversationTurn>());

        var identityConfig = new IdentityConfig
        {
            OnboardingConfidenceThreshold = 0.6,
            MaxOnboardingQuestionsPerTurn = 2,
        };
        var gatekeeper = CreateGatekeeper(
            knowledge.Object,
            sessions.Object,
            identityProvider.Object,
            onboardingDetector.Object,
            identityConfig);

        var context = await gatekeeper.GateContextAsync(
            CreateMessage("Need status"),
            CreateBudget(totalTokens: 20, conversationBudget: 5, knowledgeBudget: 5),
            "session-1");

        sequence.Take(3).Should().Equal("identity", "onboarding", "search");
        context.Identity.Should().NotBeNull();
        context.Onboarding.Should().NotBeNull();
        context.Onboarding!.OnboardingDirective.Should().Contain("Continue answering the user's current request.");
        context.BudgetUsage!.SystemPromptUsed.Should().BeGreaterThan(new SimpleTokenEstimator().EstimateTokens(context.SystemPrompt));
        context.SessionId.Should().Be("session-1");

        identityProvider.VerifyAll();
        onboardingDetector.VerifyAll();
        knowledge.VerifyAll();
        sessions.VerifyAll();
    }

    private static ContextGatekeeper CreateGatekeeper(
        IKnowledgeService knowledge,
        ISessionStore sessions,
        IIdentityProvider? identityProvider = null,
        IOnboardingDetector? onboardingDetector = null,
        IdentityConfig? identityConfig = null)
    {
        var estimator = new SimpleTokenEstimator();
        var retrievalConfig = new RetrievalConfig { ScopingEnabled = false };
        var scopedKnowledge = new Mock<IScopedKnowledgeService>(MockBehavior.Strict);
        var scopePolicy = new RetrievalScopePolicy(Options.Create(retrievalConfig), NullLogger<RetrievalScopePolicy>.Instance);
        var retriever = new ContextCandidateRetriever(
            knowledge,
            scopedKnowledge.Object,
            scopePolicy,
            sessions,
            Options.Create(retrievalConfig),
            NullLogger<ContextCandidateRetriever>.Instance);
        var historyAssembler = new ConversationHistoryAssembler(estimator, Options.Create(new ContextConfig()), NullLogger<ConversationHistoryAssembler>.Instance);
        var promptAssembler = new PromptAssembler(estimator, NullLogger<PromptAssembler>.Instance);
        var effectiveIdentityConfig = identityConfig ?? new IdentityConfig();

        return new ContextGatekeeper(
            retriever,
            historyAssembler,
            promptAssembler,
            estimator,
            Options.Create(new ContextConfig()),
            NullLogger<ContextGatekeeper>.Instance,
            identityProvider ?? new StubIdentityProvider(),
            onboardingDetector ?? new StubOnboardingDetector(),
            new OnboardingDirectiveBuilder(Options.Create(effectiveIdentityConfig)),
            Options.Create(effectiveIdentityConfig));
    }

    private static LeanKernelMessage CreateMessage(string content)
        => new() { Content = content, SenderId = "user-1", ChannelId = "channel-1" };

    private static ContextBudget CreateBudget(int totalTokens, int conversationBudget, int knowledgeBudget)
        => new()
        {
            TotalTokens = totalTokens,
            SystemPromptBudget = 2,
            WikiFactsBudget = knowledgeBudget / 2,
            ConversationBudget = conversationBudget,
            RetrievalBudget = knowledgeBudget - (knowledgeBudget / 2),
            ToolsBudget = 1,
        };

    private sealed class StubIdentityProvider : IIdentityProvider
    {
        public Task<IdentityContext> LoadIdentityAsync(string userId, CancellationToken ct = default)
            => Task.FromResult(new IdentityContext
            {
                UserId = userId,
            });
    }

    private sealed class StubOnboardingDetector : IOnboardingDetector
    {
        public Task<OnboardingResult> DetectGapsAsync(IdentityContext identity, CancellationToken ct = default)
            => Task.FromResult(new OnboardingResult());
    }
}

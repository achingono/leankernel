using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context;
using LeanKernel.Context.Retrieval;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Context;

public class ContextCandidateRetrieverTests
{
    [Fact]
    public async Task RetrieveAsync_skips_knowledge_search_for_blank_messages()
    {
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var scopedKnowledge = new Mock<IScopedKnowledgeService>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);

        sessions
            .Setup(store => store.GetHistoryAsync("session-blank", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ConversationTurn { Role = "user", Content = "Earlier" }]);

        var retriever = CreateRetriever(knowledge.Object, scopedKnowledge.Object, sessions.Object);

        var candidates = await retriever.RetrieveAsync(
            new LeanKernelMessage { Content = "   ", SenderId = "user-1", ChannelId = "channel-1" },
            "session-blank");

        candidates.KnowledgeCandidates.Should().BeEmpty();
        candidates.History.Select(turn => turn.Content).Should().Equal("Earlier");
        candidates.RetrievalDiagnostics.Should().BeNull();
        knowledge.VerifyNoOtherCalls();
        scopedKnowledge.VerifyNoOtherCalls();
        sessions.VerifyAll();
    }

    [Fact]
    public async Task RetrieveAsync_uses_scoped_retrieval_when_scoping_is_enabled()
    {
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var scopedKnowledge = new Mock<IScopedKnowledgeService>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var retrievalConfig = new RetrievalConfig { ScopingEnabled = true, DefaultScope = "global" };

        scopedKnowledge
            .Setup(service => service.RetrieveWithScopeAsync("Need status", "personal", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScopedRetrievalResult
            {
                Candidates =
                [
                    new RetrievalCandidate { Key = "doc-1", Content = "Atlas shipped", Source = "gbrain", Score = 0.8, TokenCount = 3 }
                ],
                Diagnostics = new RetrievalDiagnostics
                {
                    SessionId = "unknown",
                    TurnId = "unknown",
                    EffectiveScope = "personal",
                    Decisions =
                    [
                        new RetrievalCandidateDecision
                        {
                            Key = "doc-1",
                            Source = "gbrain",
                            OriginalScore = 0.8,
                            AdjustedScore = 1.2,
                            Admitted = true
                        }
                    ],
                    TotalConsidered = 1,
                    TotalAdmitted = 1
                }
            });
        sessions
            .Setup(store => store.GetHistoryAsync("session-1", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ConversationTurn { Role = "assistant", Content = "Earlier answer" }
            ]);

        var retriever = CreateRetriever(knowledge.Object, scopedKnowledge.Object, sessions.Object, retrievalConfig);

        var candidates = await retriever.RetrieveAsync(
            new LeanKernelMessage
            {
                Content = "Need status",
                SenderId = "user-1",
                ChannelId = "channel-1",
                Metadata = new Dictionary<string, string>
                {
                    ["retrieval_scope"] = "personal",
                    ["turn_id"] = "turn-7"
                }
            },
            "session-1");

        candidates.KnowledgeCandidates.Select(candidate => candidate.Key).Should().Equal("doc-1");
        candidates.History.Select(turn => turn.Content).Should().Equal("Earlier answer");
        candidates.RetrievalDiagnostics.Should().NotBeNull();
        candidates.RetrievalDiagnostics!.SessionId.Should().Be("session-1");
        candidates.RetrievalDiagnostics.TurnId.Should().Be("turn-7");
        candidates.RetrievalDiagnostics.EffectiveScope.Should().Be("personal");
        knowledge.VerifyNoOtherCalls();
        scopedKnowledge.VerifyAll();
        sessions.VerifyAll();
    }

    [Fact]
    public async Task RetrieveAsync_uses_raw_knowledge_search_when_scoping_is_disabled()
    {
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var scopedKnowledge = new Mock<IScopedKnowledgeService>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var retrievalConfig = new RetrievalConfig { ScopingEnabled = false };

        knowledge
            .Setup(service => service.SearchAsync("Need status", 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RetrievalCandidate { Key = "doc-1", Content = "Atlas shipped", Source = "gbrain", Score = 0.8, TokenCount = 3 }
            ]);
        sessions
            .Setup(store => store.GetHistoryAsync("session-1", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ConversationTurn { Role = "assistant", Content = "Earlier answer" }
            ]);

        var retriever = CreateRetriever(knowledge.Object, scopedKnowledge.Object, sessions.Object, retrievalConfig);

        var candidates = await retriever.RetrieveAsync(
            new LeanKernelMessage { Content = "Need status", SenderId = "user-1", ChannelId = "channel-1" },
            "session-1");

        candidates.KnowledgeCandidates.Select(candidate => candidate.Key).Should().Equal("doc-1");
        candidates.RetrievalDiagnostics.Should().BeNull();
        knowledge.VerifyAll();
        scopedKnowledge.VerifyNoOtherCalls();
        sessions.VerifyAll();
    }

    private static ContextCandidateRetriever CreateRetriever(
        IKnowledgeService knowledge,
        IScopedKnowledgeService scopedKnowledge,
        ISessionStore sessions,
        RetrievalConfig? retrievalConfig = null)
    {
        retrievalConfig ??= new RetrievalConfig();
        var scopePolicy = new RetrievalScopePolicy(Options.Create(retrievalConfig), NullLogger<RetrievalScopePolicy>.Instance);

        return new ContextCandidateRetriever(
            knowledge,
            scopedKnowledge,
            scopePolicy,
            sessions,
            Options.Create(retrievalConfig),
            NullLogger<ContextCandidateRetriever>.Instance);
    }
}

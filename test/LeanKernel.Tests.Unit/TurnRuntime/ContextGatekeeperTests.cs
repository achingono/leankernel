using FluentAssertions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.TurnRuntime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.TurnRuntime;

public class ContextGatekeeperTests
{
    private static TurnContext CreateContext(string userMessage = "hello")
    {
        return new TurnContext
        {
            Permit = CreatePermit(),
            UserMessage = userMessage,
            ConversationId = "conv-1",
        };
    }

    private static IPermit CreatePermit()
    {
        var mock = new Mock<IPermit>();
        mock.Setup(p => p.UserId).Returns(Guid.NewGuid());
        mock.Setup(p => p.TenantId).Returns(Guid.NewGuid());
        mock.Setup(p => p.ChannelId).Returns(Guid.NewGuid());
        mock.Setup(p => p.HostName).Returns("localhost");
        mock.Setup(p => p.IsAuthenticated).Returns(true);
        return mock.Object;
    }

    private static TurnPipelineSettings DefaultSettings() => new()
    {
        MaxContextTokens = 1000,
        SystemContextTokenBudget = 500,
        RetrievalTokenBudget = 300,
        MaxRetrievalCandidates = 5,
        MinRetrievalScore = 0.1,
    };

    [Fact]
    public async Task ExecuteAsync_EmptyCandidates_AdmitsNothing()
    {
        var ctx = CreateContext();
        var gatekeeper = new ContextGatekeeper(
            Options.Create(DefaultSettings()),
            Mock.Of<ILogger<ContextGatekeeper>>());

        await gatekeeper.ExecuteAsync(ctx);

        ctx.Admitted.Should().BeEmpty();
        ctx.RemainingBudget.Should().Be(1000);
    }

    [Fact]
    public async Task ExecuteAsync_SystemItem_AdmittedFirst()
    {
        var ctx = CreateContext();
        ctx.Candidates.Add(new ContextItem
        {
            Source = "system",
            Content = "You are a helpful assistant.",
            EstimatedTokens = 200,
            Score = 0.9,
        });

        var gatekeeper = new ContextGatekeeper(
            Options.Create(DefaultSettings()),
            Mock.Of<ILogger<ContextGatekeeper>>());

        await gatekeeper.ExecuteAsync(ctx);

        ctx.Admitted.Should().HaveCount(1);
        ctx.Admitted[0].Source.Should().Be("system");
        ctx.RemainingBudget.Should().Be(800);
    }

    [Fact]
    public async Task ExecuteAsync_RetrievalItem_BelowScore_NotAdmitted()
    {
        var ctx = CreateContext();
        ctx.Candidates.Add(new ContextItem
        {
            Source = "memory",
            Content = "Some fact",
            EstimatedTokens = 50,
            Score = 0.05,
        });

        var gatekeeper = new ContextGatekeeper(
            Options.Create(DefaultSettings()),
            Mock.Of<ILogger<ContextGatekeeper>>());

        await gatekeeper.ExecuteAsync(ctx);

        ctx.Admitted.Should().BeEmpty();
        ctx.AdmissionTrace.Should().ContainSingle(r =>
            r.Source == "memory" && r.Reason == "low_score");
    }

    [Fact]
    public async Task ExecuteAsync_RetrievalItem_HighScore_Admitted()
    {
        var ctx = CreateContext();
        ctx.Candidates.Add(new ContextItem
        {
            Source = "memory",
            Content = "Relevant fact",
            EstimatedTokens = 50,
            Score = 0.8,
        });

        var gatekeeper = new ContextGatekeeper(
            Options.Create(DefaultSettings()),
            Mock.Of<ILogger<ContextGatekeeper>>());

        await gatekeeper.ExecuteAsync(ctx);

        ctx.Admitted.Should().HaveCount(1);
        ctx.Admitted[0].Score.Should().Be(0.8);
    }

    [Fact]
    public async Task ExecuteAsync_BudgetExhausted_ItemRejected()
    {
        var settings = DefaultSettings();
        settings.MaxContextTokens = 100;
        var ctx = CreateContext();
        ctx.Candidates.Add(new ContextItem
        {
            Source = "system",
            Content = "Large system prompt",
            EstimatedTokens = 200,
            Score = 1.0,
        });

        var gatekeeper = new ContextGatekeeper(
            Options.Create(settings),
            Mock.Of<ILogger<ContextGatekeeper>>());

        await gatekeeper.ExecuteAsync(ctx);

        ctx.Admitted.Should().BeEmpty();
        ctx.AdmissionTrace.Should().ContainSingle(r =>
            r.Source == "system" && r.Reason == "budget_exhausted");
    }

    [Fact]
    public async Task ExecuteAsync_MaxCandidates_LimitRespected()
    {
        var settings = DefaultSettings();
        settings.MaxRetrievalCandidates = 2;
        settings.RetrievalTokenBudget = 10000;

        var ctx = CreateContext();
        for (int i = 0; i < 5; i++)
        {
            ctx.Candidates.Add(new ContextItem
            {
                Source = "memory",
                Content = $"Fact {i}",
                EstimatedTokens = 10,
                Score = 0.9 - i * 0.1,
            });
        }

        var gatekeeper = new ContextGatekeeper(
            Options.Create(settings),
            Mock.Of<ILogger<ContextGatekeeper>>());

        await gatekeeper.ExecuteAsync(ctx);

        ctx.Admitted.Should().HaveCount(2);
        ctx.AdmissionTrace.Count(r => r.Reason == "max_candidates_reached").Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_MixedSources_OrderedCorrectly()
    {
        var ctx = CreateContext();
        ctx.Candidates.Add(new ContextItem
        {
            Source = "memory",
            Content = "Fact from memory",
            EstimatedTokens = 100,
            Score = 0.9,
        });
        ctx.Candidates.Add(new ContextItem
        {
            Source = "system",
            Content = "System instructions",
            EstimatedTokens = 100,
            Score = 0.8,
        });
        ctx.Candidates.Add(new ContextItem
        {
            Source = "retrieval",
            Content = "RAG result",
            EstimatedTokens = 100,
            Score = 0.7,
        });

        var gatekeeper = new ContextGatekeeper(
            Options.Create(DefaultSettings()),
            Mock.Of<ILogger<ContextGatekeeper>>());

        await gatekeeper.ExecuteAsync(ctx);

        ctx.Admitted.Should().HaveCount(3);
        ctx.Admitted[0].Source.Should().Be("system");
    }
}

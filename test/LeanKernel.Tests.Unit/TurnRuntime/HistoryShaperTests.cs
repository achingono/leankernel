using FluentAssertions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.TurnRuntime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.TurnRuntime;

public class HistoryShaperTests
{
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

    private static TurnContext CreateContext()
    {
        return new TurnContext
        {
            Permit = CreatePermit(),
            UserMessage = "hello",
            ConversationId = "conv-1",
        };
    }

    [Fact]
    public async Task ExecuteAsync_EmptyHistory_Noop()
    {
        var shaper = new HistoryShaper(
            Options.Create(new TurnPipelineSettings { RecentTurnsVerbatim = 10 }),
            Mock.Of<ILogger<HistoryShaper>>());

        var context = CreateContext();
        await shaper.ExecuteAsync(context);

        context.ShapedHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_SmallHistory_KeepsAllVerbatim()
    {
        var shaper = new HistoryShaper(
            Options.Create(new TurnPipelineSettings { RecentTurnsVerbatim = 10 }),
            Mock.Of<ILogger<HistoryShaper>>());

        var context = CreateContext();
        for (int i = 0; i < 5; i++)
            context.ShapedHistory.Add(new ChatMessage(ChatRole.User, $"msg-{i}"));

        await shaper.ExecuteAsync(context);

        context.ShapedHistory.Should().HaveCount(5);
    }

    [Fact]
    public async Task ExecuteAsync_LargeHistory_TrimsToVerbatimWindow()
    {
        var shaper = new HistoryShaper(
            Options.Create(new TurnPipelineSettings { RecentTurnsVerbatim = 5 }),
            Mock.Of<ILogger<HistoryShaper>>());

        var context = CreateContext();
        for (int i = 0; i < 20; i++)
            context.ShapedHistory.Add(new ChatMessage(ChatRole.User, $"msg-{i}"));

        await shaper.ExecuteAsync(context);

        context.ShapedHistory.Should().HaveCount(5);
        context.ShapedHistory[0].Text.Should().Be("msg-15");
        context.ShapedHistory[4].Text.Should().Be("msg-19");
    }

    [Fact]
    public async Task ExecuteAsync_CompactionDisabled_KeepsVerbatimOnly()
    {
        var shaper = new HistoryShaper(
            Options.Create(new TurnPipelineSettings
            {
                RecentTurnsVerbatim = 5,
                CompactedTurnsMax = 10,
                EnableCompaction = false,
            }),
            Mock.Of<ILogger<HistoryShaper>>());

        var context = CreateContext();
        for (int i = 0; i < 20; i++)
            context.ShapedHistory.Add(new ChatMessage(ChatRole.User, $"msg-{i}"));

        await shaper.ExecuteAsync(context);

        context.ShapedHistory.Should().HaveCount(5);
    }
}

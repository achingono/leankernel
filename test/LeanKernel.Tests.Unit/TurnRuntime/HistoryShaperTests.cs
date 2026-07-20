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
    private static readonly ILogger<HistoryShaper> Logger = Mock.Of<ILogger<HistoryShaper>>();

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

    private static IHistorySummarizer CreateSummarizer(string? summary = null)
    {
        var mock = new Mock<IHistorySummarizer>();
        mock.Setup(service => service.SummarizeAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);
        return mock.Object;
    }

    private static IHistoryCompactor CreateCompactor(string? compacted = null)
    {
        var mock = new Mock<IHistoryCompactor>();
        mock.Setup(service => service.CompactAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(compacted);
        return mock.Object;
    }

    [Fact]
    public async Task ExecuteAsync_EmptyHistory_Noop()
    {
        var shaper = new HistoryShaper(
            Options.Create(new TurnPipelineSettings { RecentTurnsVerbatim = 10 }),
            CreateSummarizer(),
            CreateCompactor(),
            Logger);

        var context = CreateContext();
        await shaper.ExecuteAsync(context);

        context.ShapedHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_SmallHistory_KeepsAllVerbatim()
    {
        var shaper = new HistoryShaper(
            Options.Create(new TurnPipelineSettings { RecentTurnsVerbatim = 10 }),
            CreateSummarizer(),
            CreateCompactor(),
            Logger);

        var context = CreateContext();
        for (int i = 0; i < 5; i++)
        {
            context.ShapedHistory.Add(new ChatMessage(ChatRole.User, $"msg-{i}"));
        }

        await shaper.ExecuteAsync(context);

        context.ShapedHistory.Should().HaveCount(5);
    }

    [Fact]
    public async Task ExecuteAsync_LargeHistory_TrimsToVerbatimWindow()
    {
        var shaper = new HistoryShaper(
            Options.Create(new TurnPipelineSettings { RecentTurnsVerbatim = 5 }),
            CreateSummarizer(),
            CreateCompactor(),
            Logger);

        var context = CreateContext();
        for (int i = 0; i < 20; i++)
        {
            context.ShapedHistory.Add(new ChatMessage(ChatRole.User, $"msg-{i}"));
        }

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
            CreateSummarizer(),
            CreateCompactor(),
            Logger);

        var context = CreateContext();
        for (int i = 0; i < 20; i++)
        {
            context.ShapedHistory.Add(new ChatMessage(ChatRole.User, $"msg-{i}"));
        }

        await shaper.ExecuteAsync(context);

        context.ShapedHistory.Should().HaveCount(5);
    }

    [Fact]
    public async Task ExecuteAsync_CompactionEnabled_CallsCompactor()
    {
        var compactor = new Mock<IHistoryCompactor>();
        compactor.Setup(c => c.CompactAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("Compacted: key fact A. Key fact B.");

        var shaper = new HistoryShaper(
            Options.Create(new TurnPipelineSettings
            {
                RecentTurnsVerbatim = 2,
                CompactedTurnsMax = 3,
                EnableCompaction = true,
            }),
            CreateSummarizer(),
            compactor.Object,
            Logger);

        var context = CreateContext();
        for (int i = 0; i < 5; i++)
        {
            context.ShapedHistory.Add(new ChatMessage(ChatRole.User, $"msg-{i}"));
        }

        await shaper.ExecuteAsync(context);

        context.ShapedHistory.Should().HaveCount(3);
        context.ShapedHistory[0].Role.Should().Be(ChatRole.User);
        context.ShapedHistory[0].Text.Should().Contain("Historical context (compacted, untrusted)");
        context.ShapedHistory[0].Text.Should().Contain("Compacted: key fact A.");
        context.ShapedHistory[1].Text.Should().Be("msg-3");
        context.ShapedHistory[2].Text.Should().Be("msg-4");
        compactor.Verify(c => c.CompactAsync(
            It.IsAny<IReadOnlyList<ChatMessage>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CompactionUnavailable_FallsBackToVerbatim()
    {
        var shaper = new HistoryShaper(
            Options.Create(new TurnPipelineSettings
            {
                RecentTurnsVerbatim = 2,
                CompactedTurnsMax = 3,
                EnableCompaction = true,
            }),
            CreateSummarizer(),
            CreateCompactor(null),
            Logger);

        var context = CreateContext();
        for (int i = 0; i < 5; i++)
        {
            context.ShapedHistory.Add(new ChatMessage(ChatRole.User, $"msg-{i}"));
        }

        await shaper.ExecuteAsync(context);

        context.ShapedHistory.Should().HaveCount(5);
        context.ShapedHistory.Select(m => m.Text)
            .Should().ContainInOrder("msg-0", "msg-1", "msg-2", "msg-3", "msg-4");
    }

    [Fact]
    public async Task ExecuteAsync_CompactionAndSummarizationEnabled_PreservesChronologicalOrder()
    {
        var shaper = new HistoryShaper(
            Options.Create(new TurnPipelineSettings
            {
                RecentTurnsVerbatim = 2,
                CompactedTurnsMax = 2,
                SummarizedTurnsMax = 2,
                EnableCompaction = true,
                EnableSummarization = true,
            }),
            CreateSummarizer("Older summary"),
            CreateCompactor("Compacted facts"),
            Logger);

        var context = CreateContext();
        for (int i = 0; i < 6; i++)
        {
            context.ShapedHistory.Add(new ChatMessage(ChatRole.User, $"msg-{i}"));
        }

        await shaper.ExecuteAsync(context);

        context.ShapedHistory.Should().HaveCount(4);
        context.ShapedHistory[0].Role.Should().Be(ChatRole.System);
        context.ShapedHistory[0].Text.Should().Contain("Older summary");
        context.ShapedHistory[1].Role.Should().Be(ChatRole.User);
        context.ShapedHistory[1].Text.Should().Contain("Historical context (compacted, untrusted)");
        context.ShapedHistory[1].Text.Should().Contain("Compacted facts");
        context.ShapedHistory[2].Text.Should().Be("msg-4");
        context.ShapedHistory[3].Text.Should().Be("msg-5");
    }

    [Fact]
    public async Task ExecuteAsync_SummarizationEnabledAndUnavailable_FallsBackToVerbatimSummarizedRange()
    {
        var shaper = new HistoryShaper(
            Options.Create(new TurnPipelineSettings
            {
                RecentTurnsVerbatim = 2,
                CompactedTurnsMax = 2,
                SummarizedTurnsMax = 2,
                EnableCompaction = true,
                EnableSummarization = true,
            }),
            CreateSummarizer(null),
            CreateCompactor(null),
            Logger);

        var context = CreateContext();
        for (int i = 0; i < 6; i++)
        {
            context.ShapedHistory.Add(new ChatMessage(ChatRole.User, $"msg-{i}"));
        }

        await shaper.ExecuteAsync(context);

        context.ShapedHistory.Select(message => message.Text)
            .Should()
            .ContainInOrder("msg-0", "msg-1", "msg-2", "msg-3", "msg-4", "msg-5");
    }
}
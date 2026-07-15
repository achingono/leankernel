using FluentAssertions;
using LeanKernel.Logic.TurnRuntime;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.TurnRuntime;

public class TurnPipelineTests
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
    public async Task ExecuteAsync_EmptyStages_ReturnsDefaultResult()
    {
        var pipeline = new TurnPipeline([], Mock.Of<ILogger<TurnPipeline>>());
        var context = CreateContext();

        var result = await pipeline.ExecuteAsync(context);

        result.AdmittedCount.Should().Be(0);
        result.RejectedCount.Should().Be(0);
        result.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecuteAsync_WithStages_ExecutesInOrder()
    {
        var order = new List<string>();

        var stage1 = new Mock<ITurnStage>();
        stage1.Setup(s => s.Name).Returns("Stage1");
        stage1.Setup(s => s.ExecuteAsync(It.IsAny<TurnContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("Stage1"))
            .Returns(Task.CompletedTask);

        var stage2 = new Mock<ITurnStage>();
        stage2.Setup(s => s.Name).Returns("Stage2");
        stage2.Setup(s => s.ExecuteAsync(It.IsAny<TurnContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("Stage2"))
            .Returns(Task.CompletedTask);

        var pipeline = new TurnPipeline(
            [stage1.Object, stage2.Object],
            Mock.Of<ILogger<TurnPipeline>>());

        var context = CreateContext();
        await pipeline.ExecuteAsync(context);

        order.Should().BeInAscendingOrder();
        order.Should().BeEquivalentTo(["Stage1", "Stage2"]);
    }

    [Fact]
    public async Task ExecuteAsync_StageThrows_PipelineStops()
    {
        var order = new List<string>();

        var stage1 = new Mock<ITurnStage>();
        stage1.Setup(s => s.Name).Returns("Stage1");
        stage1.Setup(s => s.ExecuteAsync(It.IsAny<TurnContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("Stage1"))
            .Returns(Task.CompletedTask);

        var stage2 = new Mock<ITurnStage>();
        stage2.Setup(s => s.Name).Returns("Stage2");
        stage2.Setup(s => s.ExecuteAsync(It.IsAny<TurnContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("Stage2"))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var stage3 = new Mock<ITurnStage>();
        stage3.Setup(s => s.Name).Returns("Stage3");
        stage3.Setup(s => s.ExecuteAsync(It.IsAny<TurnContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("Stage3"))
            .Returns(Task.CompletedTask);

        var pipeline = new TurnPipeline(
            [stage1.Object, stage2.Object, stage3.Object],
            Mock.Of<ILogger<TurnPipeline>>());

        var context = CreateContext();
        var result = await pipeline.ExecuteAsync(context);

        order.Should().BeEquivalentTo(["Stage1", "Stage2"]);
        context.TerminationReason.Should().Be("stage_failure:Stage2");
        stage3.Verify(s => s.ExecuteAsync(It.IsAny<TurnContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Cancelled_StopsPipeline()
    {
        var stage1 = new Mock<ITurnStage>();
        stage1.Setup(s => s.Name).Returns("Stage1");
        stage1.Setup(s => s.ExecuteAsync(It.IsAny<TurnContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var stage2 = new Mock<ITurnStage>();
        stage2.Setup(s => s.Name).Returns("Stage2");

        var pipeline = new TurnPipeline(
            [stage1.Object, stage2.Object],
            Mock.Of<ILogger<TurnPipeline>>());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var context = CreateContext();
        var result = await pipeline.ExecuteAsync(context, cts.Token);

        context.TerminationReason.Should().Be("cancelled");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsTimingInfo()
    {
        var pipeline = new TurnPipeline([], Mock.Of<ILogger<TurnPipeline>>());
        var context = CreateContext();

        var result = await pipeline.ExecuteAsync(context);

        result.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);
        context.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);
    }
}

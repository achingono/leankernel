using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using LeanKernel.Commander;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Host.Services;

namespace LeanKernel.Tests.Unit.Host;

public class ScheduledJobExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_WithValidJob_ProcessesAndDelivers()
    {
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("scheduled response");

        var channel = new TestChannel("signal");
        var router = new ChannelRouter(
            thinker,
            [channel],
            NullLogger<ChannelRouter>.Instance);

        var executor = new ScheduledJobExecutor(
            thinker,
            router,
            NullLogger<ScheduledJobExecutor>.Instance);

        var result = await executor.ExecuteAsync(new ScheduledJobDefinition
        {
            Id = "job-a",
            Name = "A",
            PayloadMessage = "Prompt",
            DeliveryChannel = "signal",
            DeliveryRecipient = "user-a",
            OwnerUserId = "user-a",
            OwnerChannelId = "signal"
        }, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("delivered", result.DeliveryStatus);
        Assert.Equal("scheduled response", channel.LastMessage);
    }

    [Fact]
    public async Task ExecuteAsync_MissingDeliveryTarget_ReturnsFailure()
    {
        var thinker = Substitute.For<IThinkerService>();
        var router = new ChannelRouter(thinker, [], NullLogger<ChannelRouter>.Instance);
        var executor = new ScheduledJobExecutor(thinker, router, NullLogger<ScheduledJobExecutor>.Instance);

        var result = await executor.ExecuteAsync(new ScheduledJobDefinition
        {
            Id = "job-a",
            Name = "A",
            PayloadMessage = "Prompt",
            DeliveryChannel = "",
            DeliveryRecipient = "",
            OwnerUserId = "user-a",
            OwnerChannelId = "signal"
        }, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("missing_delivery_target", result.ErrorReason);
    }

    private sealed class TestChannel : IChannel
    {
        public TestChannel(string channelId)
        {
            ChannelId = channelId;
        }

        public string ChannelId { get; }
        public string? LastMessage { get; private set; }

        public bool IsAuthorizedSender(string senderId) => true;
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task SendAsync(string recipientId, string content, CancellationToken ct)
        {
            LastMessage = content;
            return Task.CompletedTask;
        }

        public Task<ChannelDeliveryResult> DeliverAsync(string recipientId, string content, CancellationToken ct = default)
        {
            LastMessage = content;
            return Task.FromResult(ChannelDeliveryResult.Successful(ChannelId, reference: "ref-1"));
        }

        public event Func<LeanKernelMessage, CancellationToken, Task>? OnMessageReceived;
    }
}

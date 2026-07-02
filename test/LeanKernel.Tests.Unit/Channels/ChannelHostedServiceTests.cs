using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Channels;

public class ChannelHostedServiceTests
{
    [Fact]
    public async Task StartAsync_and_StopAsync_manage_channel_lifecycle()
    {
        var channel = new TestChannel("signal");
        var router = new Mock<IChannelRouter>(MockBehavior.Strict);
        var service = CreateService(router.Object, channel, new ChannelsConfig());

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        channel.StartCalls.Should().Be(1);
        channel.StopCalls.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_subscribes_to_messages_and_routes_them()
    {
        var channel = new TestChannel("signal");
        var router = new Mock<IChannelRouter>(MockBehavior.Strict);
        var message = new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+15550001",
            Content = "hello"
        };

        router
            .Setup(candidate => candidate.RouteInboundAsync(message, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(router.Object, channel, new ChannelsConfig());

        await service.StartAsync(CancellationToken.None);
        await channel.PublishAsync(message);
        await service.StopAsync(CancellationToken.None);

        router.VerifyAll();
    }

    [Fact]
    public async Task StartAsync_logs_and_swallows_router_failures_for_inbound_messages()
    {
        var channel = new TestChannel("signal");
        var router = new Mock<IChannelRouter>(MockBehavior.Strict);
        var service = CreateService(router.Object, channel, new ChannelsConfig());
        var message = new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+15550001",
            Content = "hello"
        };

        router
            .Setup(candidate => candidate.RouteInboundAsync(message, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await service.StartAsync(CancellationToken.None);
        var act = () => channel.PublishAsync(message);
        await act.Should().NotThrowAsync();
        await service.StopAsync(CancellationToken.None);
    }

    private static ChannelHostedService CreateService(IChannelRouter router, IChannel channel, ChannelsConfig config)
        => new([channel], router, Options.Create(config), NullLogger<ChannelHostedService>.Instance);

    private sealed class TestChannel(string channelId) : IChannel
    {
        public string ChannelId { get; } = channelId;

        public bool IsConnected { get; private set; }

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public event Func<ChannelMessage, Task>? MessageReceived;

        public Task StartAsync(CancellationToken ct = default)
        {
            StartCalls++;
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            StopCalls++;
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task StartTypingAsync(string recipientId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task StopTypingAsync(string recipientId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendAsync(string recipientId, string message, IReadOnlyList<Attachment>? attachments = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public async Task PublishAsync(ChannelMessage message)
        {
            var handler = MessageReceived;
            if (handler is null)
            {
                return;
            }

            foreach (var subscriber in handler.GetInvocationList().Cast<Func<ChannelMessage, Task>>())
            {
                await subscriber(message);
            }
        }
    }
}

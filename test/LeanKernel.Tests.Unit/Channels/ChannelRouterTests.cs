using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Channels;

public class ChannelRouterTests
{
    [Fact]
    public async Task RouteInboundAsync_routes_authorized_message_through_runtime_and_sends_response()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var channel = new TestChannel("signal");
        var message = new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+15550001",
            Content = "hello"
        };

        runtime
            .Setup(candidate => candidate.RunTurnAsync(
                It.Is<LeanKernelMessage>(payload =>
                    payload.ChannelId == "signal"
                    && payload.SenderId == "+15550001"
                    && payload.Content == "hello"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("hi back");

        var router = CreateRouter(runtime.Object, channel, new ChannelsConfig
        {
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "signal",
                    RequireAuth = true,
                    AllowedSenders = ["+15550001"]
                }
            ]
        });

        await router.RouteInboundAsync(message);

        runtime.VerifyAll();
        channel.SentMessages.Should().ContainSingle();
        channel.SentMessages[0].RecipientId.Should().Be("+15550001");
        channel.SentMessages[0].Message.Should().Be("hi back");
        channel.TypingStarts.Should().Be(1);
        channel.TypingStops.Should().Be(1);
    }

    [Fact]
    public async Task RouteInboundAsync_rejects_unauthorized_messages_before_the_runtime_is_called()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var channel = new TestChannel("signal");
        var router = CreateRouter(runtime.Object, channel, new ChannelsConfig
        {
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "signal",
                    RequireAuth = true,
                    AllowedSenders = ["+15550001"]
                }
            ]
        });

        await router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+15550002",
            Content = "hello"
        });

        runtime.Verify(candidate => candidate.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        channel.SentMessages.Should().BeEmpty();
        channel.TypingStarts.Should().Be(0);
        channel.TypingStops.Should().Be(0);
    }

    [Fact]
    public async Task RouteInboundAsync_ignores_messages_for_unknown_channel_ids()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var channel = new TestChannel("signal");
        var router = CreateRouter(runtime.Object, channel, new ChannelsConfig
        {
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "discord",
                    RequireAuth = false
                }
            ]
        });

        await router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "discord",
            SenderId = "user-1",
            Content = "hello"
        });

        runtime.Verify(candidate => candidate.RunTurnAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        channel.SentMessages.Should().BeEmpty();
        channel.TypingStarts.Should().Be(0);
        channel.TypingStops.Should().Be(0);
    }

    private static ChannelRouter CreateRouter(IAgentRuntime runtime, IChannel channel, ChannelsConfig config)
        => new(
            runtime,
            new ChannelAuthenticator(NullLogger<ChannelAuthenticator>.Instance, Options.Create(config)),
            [channel],
            Options.Create(config),
            NullLogger<ChannelRouter>.Instance);

    private sealed class TestChannel(string channelId) : IChannel
    {
        public string ChannelId { get; } = channelId;

        public bool IsConnected { get; private set; }

        public List<(string RecipientId, string Message)> SentMessages { get; } = [];

        public int TypingStarts { get; private set; }

        public int TypingStops { get; private set; }

        public event Func<ChannelMessage, Task>? MessageReceived
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task StartTypingAsync(string recipientId, CancellationToken ct = default)
        {
            TypingStarts++;
            return Task.CompletedTask;
        }

        public Task StopTypingAsync(string recipientId, CancellationToken ct = default)
        {
            TypingStops++;
            return Task.CompletedTask;
        }

        public Task SendAsync(string recipientId, string message, CancellationToken ct = default)
        {
            SentMessages.Add((recipientId, message));
            return Task.CompletedTask;
        }
    }
}

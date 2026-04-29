using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Commander;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Commander;

public class ChannelRouterTests
{
    [Fact]
    public async Task StartAsync_StartsAllChannels()
    {
        var ch1 = CreateChannel("ch1");
        var ch2 = CreateChannel("ch2");
        var thinker = Substitute.For<IThinkerService>();

        var router = new ChannelRouter(thinker, [ch1, ch2], NullLogger<ChannelRouter>.Instance);
        await router.StartAsync(CancellationToken.None);

        await ch1.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await ch2.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_StopsAllChannels()
    {
        var ch1 = CreateChannel("ch1");
        var thinker = Substitute.For<IThinkerService>();

        var router = new ChannelRouter(thinker, [ch1], NullLogger<ChannelRouter>.Instance);
        await router.StartAsync(CancellationToken.None);
        await router.StopAsync(CancellationToken.None);

        await ch1.Received(1).StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_NoChannels_NoOp()
    {
        var thinker = Substitute.For<IThinkerService>();
        var router = new ChannelRouter(thinker, [], NullLogger<ChannelRouter>.Instance);
        await router.StartAsync(CancellationToken.None);
        // No exceptions
    }

    [Fact]
    public async Task HandleMessage_RoutesToThinkerAndSendsResponse()
    {
        Func<LeanKernelMessage, CancellationToken, Task>? capturedHandler = null;
        var ch = CreateChannelWithEventCapture("ch1", h => capturedHandler = h);
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("AI response");

        var router = new ChannelRouter(thinker, [ch], NullLogger<ChannelRouter>.Instance);
        await router.StartAsync(CancellationToken.None);

        Assert.NotNull(capturedHandler);

        var msg = new LeanKernelMessage
        {
            Id = "m1", ChannelId = "ch1", SenderId = "u1",
            Content = "Hello", Timestamp = DateTimeOffset.UtcNow
        };
        await capturedHandler!(msg, CancellationToken.None);

        await thinker.Received(1).ProcessAsync(
            Arg.Is<LeanKernelMessage>(m => m.Content == "Hello"),
            Arg.Any<CancellationToken>());
        await ch.Received(1).SendAsync("u1", "AI response", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleMessage_ThinkerThrows_DoesNotCrash()
    {
        Func<LeanKernelMessage, CancellationToken, Task>? capturedHandler = null;
        var ch = CreateChannelWithEventCapture("ch1", h => capturedHandler = h);
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new Exception("LLM error"));

        var router = new ChannelRouter(thinker, [ch], NullLogger<ChannelRouter>.Instance);
        await router.StartAsync(CancellationToken.None);

        var msg = new LeanKernelMessage
        {
            Id = "m1", ChannelId = "ch1", SenderId = "u1",
            Content = "Hello", Timestamp = DateTimeOffset.UtcNow
        };

        // Should not throw
        await capturedHandler!(msg, CancellationToken.None);
    }

    [Fact]
    public async Task HandleMessage_UnknownChannel_DoesNotSend()
    {
        Func<LeanKernelMessage, CancellationToken, Task>? capturedHandler = null;
        var ch = CreateChannelWithEventCapture("ch1", h => capturedHandler = h);
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("response");

        var router = new ChannelRouter(thinker, [ch], NullLogger<ChannelRouter>.Instance);
        await router.StartAsync(CancellationToken.None);

        // Message from a different channel ID
        var msg = new LeanKernelMessage
        {
            Id = "m1", ChannelId = "unknown_channel", SenderId = "u1",
            Content = "Hello", Timestamp = DateTimeOffset.UtcNow
        };
        await capturedHandler!(msg, CancellationToken.None);

        await ch.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static IChannel CreateChannel(string id)
    {
        var channel = Substitute.For<IChannel>();
        channel.ChannelId.Returns(id);
        return channel;
    }

    /// <summary>
    /// Creates a channel mock that captures the OnMessageReceived event handler.
    /// </summary>
    private static IChannel CreateChannelWithEventCapture(
        string id,
        Action<Func<LeanKernelMessage, CancellationToken, Task>> onHandlerSet)
    {
        var channel = Substitute.For<IChannel>();
        channel.ChannelId.Returns(id);
        channel.When(c => c.OnMessageReceived += Arg.Any<Func<LeanKernelMessage, CancellationToken, Task>>())
            .Do(ci => onHandlerSet(ci.Arg<Func<LeanKernelMessage, CancellationToken, Task>>()));
        return channel;
    }
}

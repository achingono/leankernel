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

        var router = CreateRouter(thinker, [ch1, ch2]);
        await router.StartAsync(CancellationToken.None);

        await ch1.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await ch2.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_StopsAllChannels()
    {
        var ch1 = CreateChannel("ch1");
        var thinker = Substitute.For<IThinkerService>();

        var router = CreateRouter(thinker, [ch1]);
        await router.StartAsync(CancellationToken.None);
        await router.StopAsync(CancellationToken.None);

        await ch1.Received(1).StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_NoChannels_NoOp()
    {
        var thinker = Substitute.For<IThinkerService>();
        var router = CreateRouter(thinker, []);
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

        var router = CreateRouter(thinker, [ch]);
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

        var router = CreateRouter(thinker, [ch]);
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

        var router = CreateRouter(thinker, [ch]);
        await router.StartAsync(CancellationToken.None);

        // Message from a different channel ID
        var msg = new LeanKernelMessage
        {
            Id = "m1", ChannelId = "unknown_channel", SenderId = "u1",
            Content = "Hello", Timestamp = DateTimeOffset.UtcNow
        };
        await capturedHandler!(msg, CancellationToken.None);

        await thinker.DidNotReceive().ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>());
        await ch.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleMessage_UnauthorizedSender_IsRejectedByChannelPolicy()
    {
        Func<LeanKernelMessage, CancellationToken, Task>? capturedHandler = null;
        var ch = CreateChannelWithEventCapture("ch1", h => capturedHandler = h);
        ch.IsAuthorizedSender("u1").Returns(false);
        var thinker = Substitute.For<IThinkerService>();

        var router = CreateRouter(thinker, [ch]);
        await router.StartAsync(CancellationToken.None);

        var msg = new LeanKernelMessage
        {
            Id = "m1",
            ChannelId = "ch1",
            SenderId = "u1",
            Content = "Hello",
            Timestamp = DateTimeOffset.UtcNow
        };
        await capturedHandler!(msg, CancellationToken.None);

        await thinker.DidNotReceive().ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>());
        await ch.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleMessage_AuthorizedSender_IsProcessed()
    {
        Func<LeanKernelMessage, CancellationToken, Task>? capturedHandler = null;
        var ch = CreateChannelWithEventCapture("ch1", h => capturedHandler = h);
        ch.IsAuthorizedSender("u1").Returns(true);
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("AI response");

        var router = CreateRouter(thinker, [ch]);
        await router.StartAsync(CancellationToken.None);

        var msg = new LeanKernelMessage
        {
            Id = "m1",
            ChannelId = "ch1",
            SenderId = "u1",
            Content = "Hello",
            Timestamp = DateTimeOffset.UtcNow
        };
        await capturedHandler!(msg, CancellationToken.None);

        await thinker.Received(1).ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>());
        await ch.Received(1).SendAsync("u1", "AI response", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleMessage_TypingChannel_StartsAndDisposesTypingScope()
    {
        Func<LeanKernelMessage, CancellationToken, Task>? capturedHandler = null;
        var channel = Substitute.For<IChannel, ITypingIndicatorChannel>();
        channel.ChannelId.Returns("signal");
        channel.IsAuthorizedSender(Arg.Any<string>()).Returns(true);
        channel.When(c => c.OnMessageReceived += Arg.Any<Func<LeanKernelMessage, CancellationToken, Task>>())
            .Do(ci => capturedHandler = ci.Arg<Func<LeanKernelMessage, CancellationToken, Task>>());

        var typingScope = new TrackingAsyncDisposable();
        var typingChannel = (ITypingIndicatorChannel)channel;
        typingChannel.BeginTypingAsync("u1", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IAsyncDisposable>(typingScope));

        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("AI response");

        var router = CreateRouter(thinker, [channel]);
        await router.StartAsync(CancellationToken.None);

        await capturedHandler!(new LeanKernelMessage
        {
            Id = "m1",
            ChannelId = "signal",
            SenderId = "u1",
            Content = "Hello",
            Timestamp = DateTimeOffset.UtcNow
        }, CancellationToken.None);

        await typingChannel.Received(1).BeginTypingAsync("u1", Arg.Any<CancellationToken>());
        Assert.True(typingScope.Disposed);
    }

    private static IChannel CreateChannel(string id)
    {
        var channel = Substitute.For<IChannel>();
        channel.ChannelId.Returns(id);
        channel.IsAuthorizedSender(Arg.Any<string>()).Returns(true);
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
        channel.IsAuthorizedSender(Arg.Any<string>()).Returns(true);
        channel.When(c => c.OnMessageReceived += Arg.Any<Func<LeanKernelMessage, CancellationToken, Task>>())
            .Do(ci => onHandlerSet(ci.Arg<Func<LeanKernelMessage, CancellationToken, Task>>()));
        return channel;
    }

    private static ChannelRouter CreateRouter(IThinkerService thinker, IEnumerable<IChannel> channels) =>
        new(thinker, channels, NullLogger<ChannelRouter>.Instance);

    private sealed class TrackingAsyncDisposable : IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

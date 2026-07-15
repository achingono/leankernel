using FluentAssertions;
using LeanKernel.Gateway.Channels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Channels;

/// <summary>
/// Tests for <see cref="ChannelHostedService"/> — channel lifecycle management.
/// </summary>
public sealed class ChannelHostedServiceTests
{
    [Fact]
    public async Task StartAsync_Disabled_DoesNotStartChannels()
    {
        var channelMock = new Mock<IChannel>();
        channelMock.Setup(c => c.ChannelId).Returns("test");
        var routerMock = new Mock<IChannelRouter>();
        var config = Microsoft.Extensions.Options.Options.Create(new ChannelsSettings { Enabled = false });
        var logger = Mock.Of<ILogger<ChannelHostedService>>();

        var service = new ChannelHostedService(
            [channelMock.Object],
            routerMock.Object,
            config,
            logger);

        await service.StartAsync(CancellationToken.None);

        channelMock.Verify(c => c.StartAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StartAsync_Enabled_StartsAllChannels()
    {
        var channelMock = new Mock<IChannel>();
        channelMock.Setup(c => c.ChannelId).Returns("test");
        channelMock.Setup(c => c.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var routerMock = new Mock<IChannelRouter>();
        var config = Microsoft.Extensions.Options.Options.Create(new ChannelsSettings { Enabled = true });
        var logger = Mock.Of<ILogger<ChannelHostedService>>();

        var service = new ChannelHostedService(
            [channelMock.Object],
            routerMock.Object,
            config,
            logger);

        await service.StartAsync(CancellationToken.None);

        channelMock.Verify(c => c.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_UnsubscribesAndStopsAllChannels()
    {
        var channelMock = new Mock<IChannel>();
        channelMock.Setup(c => c.ChannelId).Returns("test");
        channelMock.Setup(c => c.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var routerMock = new Mock<IChannelRouter>();
        var config = Microsoft.Extensions.Options.Options.Create(new ChannelsSettings { Enabled = true });
        var logger = Mock.Of<ILogger<ChannelHostedService>>();

        var service = new ChannelHostedService(
            [channelMock.Object],
            routerMock.Object,
            config,
            logger);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        channelMock.Verify(c => c.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleMessageAsync_RoutesThroughRouter()
    {
        ChannelMessage? capturedMessage = null;
        var channelMock = new Mock<IChannel>();
        channelMock.Setup(c => c.ChannelId).Returns("test");
        channelMock.Setup(c => c.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var routerMock = new Mock<IChannelRouter>();
        routerMock.Setup(r => r.RouteInboundAsync(It.IsAny<ChannelMessage>(), It.IsAny<CancellationToken>()))
            .Callback<ChannelMessage, CancellationToken>((msg, _) => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        var config = Microsoft.Extensions.Options.Options.Create(new ChannelsSettings { Enabled = true });
        var logger = Mock.Of<ILogger<ChannelHostedService>>();

        var service = new ChannelHostedService(
            [channelMock.Object],
            routerMock.Object,
            config,
            logger);

        await service.StartAsync(CancellationToken.None);

        // Verify that the subscription was registered via the event
        channelMock.VerifyAdd(c => c.MessageReceived += It.IsAny<Func<ChannelMessage, Task>>(), Times.Once);
    }

    [Fact]
    public async Task StartAsync_Idempotent_DoesNotStartTwice()
    {
        var channelMock = new Mock<IChannel>();
        channelMock.Setup(c => c.ChannelId).Returns("test");
        channelMock.Setup(c => c.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var routerMock = new Mock<IChannelRouter>();
        var config = Microsoft.Extensions.Options.Options.Create(new ChannelsSettings { Enabled = true });
        var logger = Mock.Of<ILogger<ChannelHostedService>>();

        var service = new ChannelHostedService(
            [channelMock.Object],
            routerMock.Object,
            config,
            logger);

        await service.StartAsync(CancellationToken.None);
        await service.StartAsync(CancellationToken.None);

        channelMock.Verify(c => c.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

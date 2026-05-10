using Microsoft.Extensions.Logging;
using NSubstitute;
using LeanKernel.Host.Services.Channels;
using Xunit;
using HostChannelDeliveryResult = LeanKernel.Host.Services.Channels.ChannelDeliveryResult;

namespace LeanKernel.Tests.Unit.Host.Channels;

public sealed class ChannelRegistryTests
{
    private readonly ChannelRegistry _registry;
    private readonly ILoggerFactory _loggerFactory;

    public ChannelRegistryTests()
    {
        _loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        _registry = new ChannelRegistry(_loggerFactory.CreateLogger<ChannelRegistry>());
    }

    [Fact]
    public void RegisterChannel_AddsChannelToRegistry()
    {
        // Arrange
        var mockChannel = CreateMockChannel("Signal", isConfigured: true);

        // Act
        _registry.RegisterChannel(mockChannel);

        // Assert
        var retrieved = _registry.GetChannel("Signal");
        Assert.NotNull(retrieved);
        Assert.Equal("Signal", retrieved.Name);
    }

    [Fact]
    public void RegisterChannel_CaseInsensitive()
    {
        // Arrange
        var mockChannel = CreateMockChannel("Discord", isConfigured: true);
        _registry.RegisterChannel(mockChannel);

        // Act
        var retrieved = _registry.GetChannel("discord");

        // Assert
        Assert.NotNull(retrieved);
    }

    [Fact]
    public void GetChannel_ReturnsNullForUnregisteredChannel()
    {
        // Act
        var result = _registry.GetChannel("UnknownChannel");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetChannel_ReturnsNullIfNotConfigured()
    {
        // Arrange
        var mockChannel = CreateMockChannel("Signal", isConfigured: false);
        _registry.RegisterChannel(mockChannel);

        // Act
        var result = _registry.GetChannel("Signal");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetChannel_ReturnsNullForNullOrEmptyName()
    {
        // Act
        var resultNull = _registry.GetChannel(null!);
        var resultEmpty = _registry.GetChannel("");
        var resultWhitespace = _registry.GetChannel("   ");

        // Assert
        Assert.Null(resultNull);
        Assert.Null(resultEmpty);
        Assert.Null(resultWhitespace);
    }

    [Fact]
    public void GetAllChannels_ReturnsAllRegisteredChannels()
    {
        // Arrange
        var signal = CreateMockChannel("Signal", isConfigured: true);
        var discord = CreateMockChannel("Discord", isConfigured: true);
        _registry.RegisterChannel(signal);
        _registry.RegisterChannel(discord);

        // Act
        var allChannels = _registry.GetAllChannels();

        // Assert
        Assert.Equal(2, allChannels.Count);
        Assert.True(allChannels.ContainsKey("Signal"));
        Assert.True(allChannels.ContainsKey("Discord"));
    }

    [Fact]
    public void RegisteredChannelCount_ReturnsCorrectCount()
    {
        // Arrange
        var channel1 = CreateMockChannel("Signal", isConfigured: true);
        var channel2 = CreateMockChannel("Discord", isConfigured: true);

        // Act
        _registry.RegisterChannel(channel1);
        Assert.Equal(1, _registry.RegisteredChannelCount);
        _registry.RegisterChannel(channel2);

        // Assert
        Assert.Equal(2, _registry.RegisteredChannelCount);
    }

    [Fact]
    public void IsChannelAvailable_ReturnsTrueForConfiguredChannel()
    {
        // Arrange
        var channel = CreateMockChannel("Signal", isConfigured: true);
        _registry.RegisterChannel(channel);

        // Act
        var result = _registry.IsChannelAvailable("Signal");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsChannelAvailable_ReturnsFalseForUnconfiguredChannel()
    {
        // Arrange
        var channel = CreateMockChannel("Signal", isConfigured: false);
        _registry.RegisterChannel(channel);

        // Act
        var result = _registry.IsChannelAvailable("Signal");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RegisterChannel_OverwritesPreviousRegistration()
    {
        // Arrange
        var channel1 = CreateMockChannel("Signal", isConfigured: true);
        var channel2 = CreateMockChannel("Signal", isConfigured: true);
        _registry.RegisterChannel(channel1);

        // Act
        _registry.RegisterChannel(channel2);

        // Assert
        Assert.Equal(1, _registry.RegisteredChannelCount);
        var retrieved = _registry.GetChannel("Signal");
        Assert.Same(channel2, retrieved);
    }

    private static IMessageChannel CreateMockChannel(string name, bool isConfigured)
    {
        var mock = Substitute.For<IMessageChannel>();
        mock.Name.Returns(name);
        mock.IsConfigured.Returns(isConfigured);
        mock.DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(HostChannelDeliveryResult.Successful(name));

        return mock;
    }
}

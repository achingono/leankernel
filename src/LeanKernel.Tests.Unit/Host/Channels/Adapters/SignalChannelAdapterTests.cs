using Microsoft.Extensions.Logging;
using NSubstitute;
using LeanKernel.Host.Services.Channels.Adapters;
using Xunit;

namespace LeanKernel.Tests.Unit.Host.Channels.Adapters;

public sealed class SignalChannelAdapterTests
{
    private readonly ILoggerFactory _loggerFactory;

    public SignalChannelAdapterTests()
    {
        _loggerFactory = LoggerFactory.Create(b => b.AddConsole());
    }

    [Fact]
    public void IsConfigured_ReturnsTrueWhenAllCredentialsPresent()
    {
        // Arrange & Act
        var adapter = CreateAdapter(
            cliPath: "/usr/bin/signal-cli",
            account: "+1234567890");

        // Assert
        Assert.True(adapter.IsConfigured);
    }

    [Theory]
    [InlineData(null, "+1234567890")]
    [InlineData("/usr/bin/signal-cli", null)]
    [InlineData("", "+1234567890")]
    [InlineData("/usr/bin/signal-cli", "")]
    public void IsConfigured_ReturnsFalseWhenMissingCredentials(string? cliPath, string? account)
    {
        // Arrange & Act
        var adapter = CreateAdapter(cliPath, account);

        // Assert
        Assert.False(adapter.IsConfigured);
    }

    [Fact]
    public async Task DeliverAsync_ReturnsFailureWhenNotConfigured()
    {
        // Arrange
        var adapter = CreateAdapter(null, null);

        // Act
        var result = await adapter.DeliverAsync("+recipient", "test message");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Signal channel is not configured", result.Error);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public async Task DeliverAsync_ReturnsFailureForEmptyRecipient()
    {
        // Arrange
        var adapter = CreateAdapter(
            cliPath: "/usr/bin/signal-cli",
            account: "+1234567890");

        // Act
        var result = await adapter.DeliverAsync("", "test message");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("phone number", result.Error!);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public async Task DeliverAsync_ReturnsFailureForEmptyContent()
    {
        // Arrange
        var adapter = CreateAdapter(
            cliPath: "/usr/bin/signal-cli",
            account: "+1234567890");

        // Act
        var result = await adapter.DeliverAsync("+recipient", "");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("empty", result.Error!);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public void SignalChannelAdapter_HasCorrectName()
    {
        // Arrange & Act
        var adapter = CreateAdapter(
            cliPath: "/usr/bin/signal-cli",
            account: "+1234567890");

        // Assert
        Assert.Equal("Signal", adapter.Name);
    }

    private SignalChannelAdapter CreateAdapter(string? cliPath, string? account)
    {
        return new SignalChannelAdapter(
            _loggerFactory.CreateLogger<SignalChannelAdapter>(),
            cliPath,
            account);
    }
}

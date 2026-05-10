using Microsoft.Extensions.Logging;
using LeanKernel.Commander.Adapters;
using Xunit;

namespace LeanKernel.Tests.Unit.Commander;

public sealed class DiscordChannelAdapterTests
{
    private readonly ILoggerFactory _loggerFactory;

    public DiscordChannelAdapterTests()
    {
        _loggerFactory = LoggerFactory.Create(b => b.AddConsole());
    }

    [Fact]
    public void IsConfigured_ReturnsTrueWhenAllCredentialsPresent()
    {
        // Arrange & Act
        var adapter = CreateAdapter(botToken: "token123", channelId: "456");

        // Assert
        Assert.True(adapter.IsConfigured);
    }

    [Theory]
    [InlineData(null, "channel-id")]
    [InlineData("token", null)]
    [InlineData("", "channel-id")]
    [InlineData("token", "")]
    public void IsConfigured_ReturnsFalseWhenMissingCredentials(string? token, string? channelId)
    {
        // Arrange & Act
        var adapter = CreateAdapter(token, channelId);

        // Assert
        Assert.False(adapter.IsConfigured);
    }

    [Fact]
    public async Task DeliverAsync_ReturnsFailureWhenNotConfigured()
    {
        // Arrange
        var adapter = CreateAdapter(null, null);

        // Act
        var result = await adapter.DeliverAsync("@user", "test message");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Discord channel is not configured", result.Error);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public async Task DeliverAsync_ReturnsFailureForEmptyContent()
    {
        // Arrange
        var adapter = CreateAdapter("token", "channel-id");

        // Act
        var result = await adapter.DeliverAsync("@user", "");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("empty", result.Error!);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public async Task DeliverAsync_SuccessfulDelivery()
    {
        // Arrange
        var messageHandler = new MockDiscordHttpMessageHandler();
        messageHandler.ResponseContent = """{"id":"123456","content":"test message","channel_id":"456"}""";
        var httpClient = new HttpClient(messageHandler);

        var adapter = new DiscordChannelAdapter(
            _loggerFactory.CreateLogger<DiscordChannelAdapter>(),
            httpClient,
            botToken: "token123",
            channelId: "456");

        // Act
        var result = await adapter.DeliverAsync("@user", "test message");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("123456", result.DeliveryReference);
        Assert.Equal("Discord", result.Channel);
    }

    [Fact]
    public async Task DeliverAsync_RetryableOnNetworkError()
    {
        // Arrange
        var messageHandler = new MockDiscordHttpMessageHandler();
        messageHandler.ThrowHttpException = true;
        var httpClient = new HttpClient(messageHandler);

        var adapter = new DiscordChannelAdapter(
            _loggerFactory.CreateLogger<DiscordChannelAdapter>(),
            httpClient,
            botToken: "token123",
            channelId: "456");

        // Act
        var result = await adapter.DeliverAsync("@user", "test message");

        // Assert
        Assert.False(result.Success);
        Assert.True(result.IsRetryable);
        Assert.NotNull(result.SuggestedRetryDelay);
    }

    [Fact]
    public async Task DeliverAsync_HandlesUnauthorized()
    {
        // Arrange
        var messageHandler = new MockDiscordHttpMessageHandler();
        messageHandler.StatusCode = System.Net.HttpStatusCode.Unauthorized;
        messageHandler.ResponseContent = """{"message":"Unauthorized"}""";
        var httpClient = new HttpClient(messageHandler);

        var adapter = new DiscordChannelAdapter(
            _loggerFactory.CreateLogger<DiscordChannelAdapter>(),
            httpClient,
            botToken: "invalid-token",
            channelId: "456");

        // Act
        var result = await adapter.DeliverAsync("@user", "test message");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("401", result.Error!);
    }

    [Fact]
    public async Task DeliverAsync_HandlesRateLimit()
    {
        // Arrange
        var messageHandler = new MockDiscordHttpMessageHandler();
        messageHandler.StatusCode = System.Net.HttpStatusCode.TooManyRequests;
        messageHandler.ResponseContent = """{"retry_after":5}""";
        messageHandler.RetryAfterHeader = "5";
        var httpClient = new HttpClient(messageHandler);

        var adapter = new DiscordChannelAdapter(
            _loggerFactory.CreateLogger<DiscordChannelAdapter>(),
            httpClient,
            botToken: "token123",
            channelId: "456");

        // Act
        var result = await adapter.DeliverAsync("@user", "test message");

        // Assert
        Assert.False(result.Success);
        Assert.True(result.IsRetryable);
        Assert.NotNull(result.SuggestedRetryDelay);
        Assert.True(result.SuggestedRetryDelay.Value.TotalSeconds >= 5);
    }

    [Fact]
    public async Task DeliverAsync_RetriesAfterRateLimit()
    {
        // Arrange
        var messageHandler = new MockDiscordHttpMessageHandler();
        messageHandler.FailWithRateLimitThenSucceed = true;
        messageHandler.ResponseContent = """{"id":"789"}""";
        messageHandler.RetryAfterHeader = "1";
        var httpClient = new HttpClient(messageHandler);

        var adapter = new DiscordChannelAdapter(
            _loggerFactory.CreateLogger<DiscordChannelAdapter>(),
            httpClient,
            botToken: "token123",
            channelId: "456");

        // Act
        var result = await adapter.DeliverAsync("@user", "test message");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("789", result.DeliveryReference);
        Assert.True(messageHandler.RequestCount >= 2);
    }

    [Fact]
    public void DiscordChannelAdapter_HasCorrectName()
    {
        // Arrange & Act
        var adapter = CreateAdapter("token", "channel-id");

        // Assert
        Assert.Equal("Discord", adapter.Name);
    }

    private DiscordChannelAdapter CreateAdapter(string? botToken, string? channelId)
    {
        var httpClient = new HttpClient();
        return new DiscordChannelAdapter(
            _loggerFactory.CreateLogger<DiscordChannelAdapter>(),
            httpClient,
            botToken,
            channelId);
    }
}

internal sealed class MockDiscordHttpMessageHandler : HttpMessageHandler
{
    public string ResponseContent { get; set; } = "{}";
    public System.Net.HttpStatusCode StatusCode { get; set; } = System.Net.HttpStatusCode.OK;
    public bool ThrowHttpException { get; set; }
    public bool FailWithRateLimitThenSucceed { get; set; }
    public string? RetryAfterHeader { get; set; }
    public int RequestCount { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;

        if (ThrowHttpException)
        {
            throw new HttpRequestException("Network error");
        }

        if (FailWithRateLimitThenSucceed && RequestCount == 1)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(ResponseContent)
            };
            if (!string.IsNullOrEmpty(RetryAfterHeader))
            {
                response.Headers.Add("Retry-After", RetryAfterHeader);
            }
            return response;
        }

        await Task.Delay(10, cancellationToken);
        var successResponse = new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(ResponseContent)
        };

        if (!string.IsNullOrEmpty(RetryAfterHeader) && StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            successResponse.Headers.Add("Retry-After", RetryAfterHeader);
        }

        return successResponse;
    }
}

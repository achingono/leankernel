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
            phoneNumber: "+1234567890",
            serverUrl: "https://signal.example.com",
            apiToken: "test-token");

        // Assert
        Assert.True(adapter.IsConfigured);
    }

    [Theory]
    [InlineData(null, "https://signal.example.com", "token")]
    [InlineData("+1234567890", null, "token")]
    [InlineData("+1234567890", "https://signal.example.com", null)]
    [InlineData("", "https://signal.example.com", "token")]
    public void IsConfigured_ReturnsFalseWhenMissingCredentials(string? phone, string? url, string? token)
    {
        // Arrange & Act
        var adapter = CreateAdapter(phone, url, token);

        // Assert
        Assert.False(adapter.IsConfigured);
    }

    [Fact]
    public async Task DeliverAsync_ReturnsFailureWhenNotConfigured()
    {
        // Arrange
        var adapter = CreateAdapter(null, null, null);

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
            phoneNumber: "+1234567890",
            serverUrl: "https://signal.example.com",
            apiToken: "token");

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
            phoneNumber: "+1234567890",
            serverUrl: "https://signal.example.com",
            apiToken: "token");

        // Act
        var result = await adapter.DeliverAsync("+recipient", "");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("empty", result.Error!);
        Assert.False(result.IsRetryable);
    }

    [Fact]
    public async Task DeliverAsync_SuccessfulDelivery()
    {
        // Arrange
        var messageHandler = new MockHttpMessageHandler();
        messageHandler.ResponseContent = """{"id":"msg-123","status":"sent"}""";
        var httpClient = new HttpClient(messageHandler);

        var adapter = new SignalChannelAdapter(
            _loggerFactory.CreateLogger<SignalChannelAdapter>(),
            httpClient,
            phoneNumber: "+1234567890",
            serverUrl: "https://signal.example.com",
            apiToken: "test-token");

        // Act
        var result = await adapter.DeliverAsync("+recipient", "test message");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.DeliveryReference);
        Assert.Equal("Signal", result.Channel);
    }

    [Fact]
    public async Task DeliverAsync_RetryableOnNetworkError()
    {
        // Arrange
        var messageHandler = new MockHttpMessageHandler();
        messageHandler.ThrowHttpException = true;
        var httpClient = new HttpClient(messageHandler);

        var adapter = new SignalChannelAdapter(
            _loggerFactory.CreateLogger<SignalChannelAdapter>(),
            httpClient,
            phoneNumber: "+1234567890",
            serverUrl: "https://signal.example.com",
            apiToken: "test-token");

        // Act
        var result = await adapter.DeliverAsync("+recipient", "test message");

        // Assert
        Assert.False(result.Success);
        Assert.True(result.IsRetryable);
        Assert.NotNull(result.SuggestedRetryDelay);
        Assert.True(result.SuggestedRetryDelay.Value.TotalSeconds > 0);
    }

    [Fact]
    public async Task DeliverAsync_HandlesHttpErrorResponse()
    {
        // Arrange
        var messageHandler = new MockHttpMessageHandler();
        messageHandler.StatusCode = System.Net.HttpStatusCode.Unauthorized;
        messageHandler.ResponseContent = "Unauthorized";
        var httpClient = new HttpClient(messageHandler);

        var adapter = new SignalChannelAdapter(
            _loggerFactory.CreateLogger<SignalChannelAdapter>(),
            httpClient,
            phoneNumber: "+1234567890",
            serverUrl: "https://signal.example.com",
            apiToken: "invalid-token");

        // Act
        var result = await adapter.DeliverAsync("+recipient", "test message");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Unauthorized", result.Error!);
    }

    [Fact]
    public async Task DeliverAsync_RetriesOnTransientFailure()
    {
        // Arrange
        var messageHandler = new MockHttpMessageHandler();
        messageHandler.FailThenSucceed = true;
        messageHandler.ResponseContent = """{"id":"msg-456"}""";
        var httpClient = new HttpClient(messageHandler);

        var adapter = new SignalChannelAdapter(
            _loggerFactory.CreateLogger<SignalChannelAdapter>(),
            httpClient,
            phoneNumber: "+1234567890",
            serverUrl: "https://signal.example.com",
            apiToken: "test-token");

        // Act
        var result = await adapter.DeliverAsync("+recipient", "test message");

        // Assert
        Assert.True(result.Success);
        Assert.True(messageHandler.RequestCount >= 2);
    }

    [Fact]
    public void SignalChannelAdapter_HasCorrectName()
    {
        // Arrange & Act
        var adapter = CreateAdapter(
            phoneNumber: "+1234567890",
            serverUrl: "https://signal.example.com",
            apiToken: "token");

        // Assert
        Assert.Equal("Signal", adapter.Name);
    }

    private SignalChannelAdapter CreateAdapter(string? phoneNumber, string? serverUrl, string? apiToken)
    {
        var httpClient = new HttpClient();
        return new SignalChannelAdapter(
            _loggerFactory.CreateLogger<SignalChannelAdapter>(),
            httpClient,
            phoneNumber,
            serverUrl,
            apiToken);
    }
}

internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    public string ResponseContent { get; set; } = "{}";
    public System.Net.HttpStatusCode StatusCode { get; set; } = System.Net.HttpStatusCode.OK;
    public bool ThrowHttpException { get; set; }
    public bool FailThenSucceed { get; set; }
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

        if (FailThenSucceed && RequestCount == 1)
        {
            throw new HttpRequestException("Transient error");
        }

        await Task.Delay(10, cancellationToken);
        return new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(ResponseContent)
        };
    }
}

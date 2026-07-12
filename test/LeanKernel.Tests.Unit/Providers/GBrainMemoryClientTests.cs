using FluentAssertions;
using LeanKernel.Gateway.Providers;
using LeanKernel.Logic.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Providers;

public class GBrainMemoryClientTests
{
    private static MemoryScope CreateScope(
        Guid? tenantId = null,
        Guid? userId = null,
        Guid? channelId = null)
    {
        return new MemoryScope
        {
            TenantId = tenantId ?? Guid.NewGuid(),
            UserId = userId ?? Guid.NewGuid(),
            ChannelId = channelId ?? Guid.NewGuid()
        };
    }

    [Fact]
    public void Constructor_NullClient_Throws()
    {
        var act = () => new GBrainMemoryClient(null!, Mock.Of<ILogger<GBrainMemoryClient>>());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var mockClient = new Mock<IGBrainMcpClient>();

        var act = () => new GBrainMemoryClient(mockClient.Object, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task SearchMemoriesAsync_WhenClientThrows_ReturnsEmpty()
    {
        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GBrainException("service unavailable"));

        var client = new GBrainMemoryClient(mockClient.Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var scope = CreateScope();

        var results = await client.SearchMemoriesAsync(scope, "test query");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchMemoriesAsync_NullResult_ReturnsEmpty()
    {
        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Text.Json.JsonElement?)null);

        var client = new GBrainMemoryClient(mockClient.Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var scope = CreateScope();

        var results = await client.SearchMemoriesAsync(scope, "test query");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveMemoryAsync_WhenClientThrows_PropagatesException()
    {
        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GBrainException("write failed"));

        var client = new GBrainMemoryClient(mockClient.Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var scope = CreateScope();

        var act = async () => await client.SaveMemoryAsync(scope, "key", "content");

        await act.Should().ThrowAsync<GBrainException>();
    }

    [Fact]
    public async Task SaveMemoryAsync_Success_CallsPutPage()
    {
        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Text.Json.JsonElement?)null);

        var client = new GBrainMemoryClient(mockClient.Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var scope = CreateScope();

        await client.SaveMemoryAsync(scope, "test-key", "test content");

        mockClient.Verify(c => c.CallToolAsync(
            "put_page",
            It.IsAny<object?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchMemoriesAsync_Success_CallsSearch()
    {
        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Text.Json.JsonElement?)null);

        var client = new GBrainMemoryClient(mockClient.Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var scope = CreateScope();

        await client.SearchMemoriesAsync(scope, "query", 5);

        mockClient.Verify(c => c.CallToolAsync(
            "search",
            It.IsAny<object?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveMemoryAsync_CallsPutPageWithCorrectSlug()
    {
        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Text.Json.JsonElement?)null);

        var client = new GBrainMemoryClient(mockClient.Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var scope = CreateScope(
            tenantId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            userId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            channelId: Guid.Parse("33333333-3333-3333-3333-333333333333"));

        await client.SaveMemoryAsync(scope, "my-key", "content");

        mockClient.Verify(c => c.CallToolAsync(
            "put_page",
            It.Is<object>(args =>
                args!.ToString()!.Contains("memory/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222/33333333-3333-3333-3333-333333333333/my-key")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

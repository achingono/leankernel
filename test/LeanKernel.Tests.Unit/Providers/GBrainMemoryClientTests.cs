using FluentAssertions;
using LeanKernel.Gateway.Providers;
using LeanKernel.Logic.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Providers;

/// <summary>
/// Covers the GBrain-backed memory client.
/// </summary>
public class GBrainMemoryClientTests
{
    /// <summary>
    /// Creates a scope with generated identifiers unless provided.
    /// </summary>
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

    /// <summary>
    /// Verifies the constructor rejects a missing MCP client.
    /// </summary>
    [Fact]
    public void Constructor_NullClient_Throws()
    {
        var act = () => new GBrainMemoryClient(null!, Mock.Of<ILogger<GBrainMemoryClient>>());

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Verifies the constructor rejects a missing logger.
    /// </summary>
    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var mockClient = new Mock<IGBrainMcpClient>();

        var act = () => new GBrainMemoryClient(mockClient.Object, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Verifies search failures are handled by returning no memories.
    /// </summary>
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

    /// <summary>
    /// Verifies null search results are treated as empty.
    /// </summary>
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

    /// <summary>
    /// Verifies save failures are propagated to callers.
    /// </summary>
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

    /// <summary>
    /// Verifies successful saves invoke the put page tool.
    /// </summary>
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

    /// <summary>
    /// Verifies successful searches invoke the search tool.
    /// </summary>
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

    /// <summary>
    /// Verifies saved memory keys include the expected scoped slug.
    /// </summary>
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

    [Fact]
    public async Task SearchMemoriesAsync_WrappedResultFormat_ParsesCorrectly()
    {
        var json = System.Text.Json.JsonDocument.Parse(
            """{"results":[{"slug":"test/key","compiled_truth":"content","score":0.9}]}""")
            .RootElement.Clone();

        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync("search", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var client = new GBrainMemoryClient(mockClient.Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var scope = CreateScope();

        var results = await client.SearchMemoriesAsync(scope, "test");

        results.Should().HaveCount(1);
        results[0].Key.Should().Be("test/key");
        results[0].Text.Should().Be("content");
        results[0].Score.Should().Be(0.9);
    }

    [Fact]
    public async Task SearchMemoriesAsync_ArrayFormat_ParsesCorrectly()
    {
        var json = System.Text.Json.JsonDocument.Parse(
            """[{"slug":"a","compiled_truth":"content a","score":0.8}]""")
            .RootElement.Clone();

        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync("search", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var client = new GBrainMemoryClient(mockClient.Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var scope = CreateScope();

        var results = await client.SearchMemoriesAsync(scope, "test");

        results.Should().HaveCount(1);
        results[0].Key.Should().Be("a");
        results[0].Source.Should().Be("gbrain");
    }
}

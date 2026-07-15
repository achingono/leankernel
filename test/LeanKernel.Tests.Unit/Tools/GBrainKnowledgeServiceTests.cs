using System.Text.Json;
using FluentAssertions;
using LeanKernel.Gateway.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class GBrainKnowledgeServiceTests
{
    private readonly Mock<IGBrainMcpClient> _mockClient = new();
    private readonly ILogger<GBrainService> _logger =
        new Mock<ILogger<GBrainService>>().Object;

    private GBrainService CreateService() =>
        new(_mockClient.Object, _logger);

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task SearchAsync_ReturnsResults()
    {
        var json = ParseJson("""[{"slug":"page/1","compiled_truth":"Content one","score":0.9}]""");
        _mockClient.Setup(c => c.CallToolAsync("search", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var service = CreateService();
        var results = await service.SearchAsync("test query");

        results.Should().HaveCount(1);
        results[0].Key.Should().Be("page/1");
        results[0].Content.Should().Be("Content one");
        results[0].Score.Should().Be(0.9);
    }

    [Fact]
    public async Task SearchAsync_NullResult_ReturnsEmpty()
    {
        _mockClient.Setup(c => c.CallToolAsync("search", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JsonElement?)null);

        var service = CreateService();
        var results = await service.SearchAsync("test");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_GBrainException_ReturnsEmpty()
    {
        _mockClient.Setup(c => c.CallToolAsync("search", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GBrainException("Search failed", -1));

        var service = CreateService();
        var results = await service.SearchAsync("test");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPageAsync_ReturnsPage()
    {
        var json = ParseJson("""{"slug":"docs/readme","compiled_truth":"# README","updated_at":"2024-01-01T00:00:00Z"}""");
        _mockClient.Setup(c => c.CallToolAsync("get_page", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var service = CreateService();
        var page = await service.GetPageAsync("docs/readme");

        page.Should().NotBeNull();
        page!.Key.Should().Be("docs/readme");
        page.Content.Should().Be("# README");
    }

    [Fact]
    public async Task GetPageAsync_NullResult_ReturnsNull()
    {
        _mockClient.Setup(c => c.CallToolAsync("get_page", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JsonElement?)null);

        var service = CreateService();
        var page = await service.GetPageAsync("docs/missing");

        page.Should().BeNull();
    }

    [Fact]
    public async Task GetPageAsync_GBrainException_ReturnsNull()
    {
        _mockClient.Setup(c => c.CallToolAsync("get_page", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GBrainException("Not found", 404));

        var service = CreateService();
        var page = await service.GetPageAsync("missing");

        page.Should().BeNull();
    }

    [Fact]
    public async Task PutPageAsync_CallsGBrain()
    {
        _mockClient.Setup(c => c.CallToolAsync("put_page", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JsonElement?)null);

        var service = CreateService();
        await service.PutPageAsync("wiki/test", "# Test content");

        _mockClient.Verify(c => c.CallToolAsync(
            "put_page",
            It.Is<object>(o => o.ToString()!.Contains("wiki/test")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WrappedResultsFormat_ParsesCorrectly()
    {
        var json = ParseJson("""{"results":[{"slug":"a","compiled_truth":"content a","score":0.8}]}""");
        _mockClient.Setup(c => c.CallToolAsync("search", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var service = CreateService();
        var results = await service.SearchAsync("query");

        results.Should().HaveCount(1);
        results[0].Key.Should().Be("a");
    }

    [Fact]
    public void Constructor_NullClient_Throws()
    {
        var act = () => new GBrainService(null!, _logger);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new GBrainService(_mockClient.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

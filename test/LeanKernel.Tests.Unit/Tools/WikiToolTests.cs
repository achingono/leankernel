using System.Text.Json;
using FluentAssertions;
using LeanKernel.Logic.Memory;
using LeanKernel.Logic.Tools.Memory;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class WikiToolTests
{
    private IServiceScopeFactory BuildScopeFactory(IMemoryService memoryService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(memoryService);
        var sp = services.BuildServiceProvider();

        var mockFactory = new Mock<IServiceScopeFactory>();
        mockFactory.Setup(f => f.CreateScope())
            .Returns(() =>
            {
                var mockScope = new Mock<IServiceScope>();
                mockScope.Setup(s => s.ServiceProvider).Returns(sp);
                return mockScope.Object;
            });

        return mockFactory.Object;
    }

    // MemorySearchTool

    [Fact]
    public async Task WikiSearch_ReturnsResults()
    {
        var mockKnowledge = new Mock<IMemoryService>();
        mockKnowledge.Setup(k => k.SearchAsync("test", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new MemorySearchResult { Key = "page/1", Content = "Content", Score = 0.9 }]);

        var tool = MemorySearchTool.Create(BuildScopeFactory(mockKnowledge.Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["query"] = "test" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("page/1");
    }

    [Fact]
    public async Task WikiSearch_MissingQuery_ReturnsError()
    {
        var mockKnowledge = new Mock<IMemoryService>();
        var tool = MemorySearchTool.Create(BuildScopeFactory(mockKnowledge.Object));

        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("required");
    }

    [Fact]
    public async Task WikiSearch_Exception_ReturnsError()
    {
        var mockKnowledge = new Mock<IMemoryService>();
        mockKnowledge.Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("GBrain error"));

        var tool = MemorySearchTool.Create(BuildScopeFactory(mockKnowledge.Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["query"] = "test" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("GBrain error");
    }

    [Fact]
    public void WikiSearch_Properties_AreCorrect()
    {
        var tool = MemorySearchTool.Create(BuildScopeFactory(new Mock<IMemoryService>().Object));
        tool.Name.Should().Be("memory_search");
        tool.Category.Should().Be("knowledge");
    }

    // MemoryReadTool

    [Fact]
    public async Task WikiRead_ReturnsPage()
    {
        var mockKnowledge = new Mock<IMemoryService>();
        mockKnowledge.Setup(k => k.GetPageAsync("docs/readme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryPage { Key = "docs/readme", Content = "# README" });

        var tool = MemoryReadTool.Create(BuildScopeFactory(mockKnowledge.Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["key"] = "docs/readme" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("README");
    }

    [Fact]
    public async Task WikiRead_PageNotFound_ReturnsError()
    {
        var mockKnowledge = new Mock<IMemoryService>();
        mockKnowledge.Setup(k => k.GetPageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MemoryPage?)null);

        var tool = MemoryReadTool.Create(BuildScopeFactory(mockKnowledge.Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["key"] = "missing" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task WikiRead_MissingKey_ReturnsError()
    {
        var tool = MemoryReadTool.Create(BuildScopeFactory(new Mock<IMemoryService>().Object));
        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("required");
    }

    // MemoryWriteTool

    [Fact]
    public async Task WikiWrite_SavesPage()
    {
        var mockKnowledge = new Mock<IMemoryService>();
        mockKnowledge.Setup(k => k.PutPageAsync("wiki/test", "# Content", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tool = MemoryWriteTool.Create(BuildScopeFactory(mockKnowledge.Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["key"] = "wiki/test", ["content"] = "# Content" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("saved");
        mockKnowledge.Verify(k => k.PutPageAsync("wiki/test", "# Content", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WikiWrite_MissingKey_ReturnsError()
    {
        var tool = MemoryWriteTool.Create(BuildScopeFactory(new Mock<IMemoryService>().Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["content"] = "content" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("key");
    }

    [Fact]
    public async Task WikiWrite_MissingContent_ReturnsError()
    {
        var tool = MemoryWriteTool.Create(BuildScopeFactory(new Mock<IMemoryService>().Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["key"] = "wiki/test" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("content");
    }
}

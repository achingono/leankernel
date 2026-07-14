using System.Text.Json;
using FluentAssertions;
using LeanKernel.Gateway.Providers;
using LeanKernel.Gateway.Tools.Wiki;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class WikiToolTests
{
    private IServiceScopeFactory BuildScopeFactory(IKnowledgeService knowledge)
    {
        var services = new ServiceCollection();
        services.AddSingleton(knowledge);
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

    // WikiSearchTool

    [Fact]
    public async Task WikiSearch_ReturnsResults()
    {
        var mockKnowledge = new Mock<IKnowledgeService>();
        mockKnowledge.Setup(k => k.SearchAsync("test", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new KnowledgeSearchResult { Key = "page/1", Content = "Content", Score = 0.9 }]);

        var tool = WikiSearchTool.Create(BuildScopeFactory(mockKnowledge.Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["query"] = "test" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("page/1");
    }

    [Fact]
    public async Task WikiSearch_MissingQuery_ReturnsError()
    {
        var mockKnowledge = new Mock<IKnowledgeService>();
        var tool = WikiSearchTool.Create(BuildScopeFactory(mockKnowledge.Object));

        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("required");
    }

    [Fact]
    public async Task WikiSearch_Exception_ReturnsError()
    {
        var mockKnowledge = new Mock<IKnowledgeService>();
        mockKnowledge.Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("GBrain error"));

        var tool = WikiSearchTool.Create(BuildScopeFactory(mockKnowledge.Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["query"] = "test" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("GBrain error");
    }

    [Fact]
    public void WikiSearch_Properties_AreCorrect()
    {
        var tool = WikiSearchTool.Create(BuildScopeFactory(new Mock<IKnowledgeService>().Object));
        tool.Name.Should().Be("wiki_search");
        tool.Category.Should().Be("knowledge");
    }

    // WikiReadTool

    [Fact]
    public async Task WikiRead_ReturnsPage()
    {
        var mockKnowledge = new Mock<IKnowledgeService>();
        mockKnowledge.Setup(k => k.GetPageAsync("docs/readme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgePage { Key = "docs/readme", Content = "# README" });

        var tool = WikiReadTool.Create(BuildScopeFactory(mockKnowledge.Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["key"] = "docs/readme" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("README");
    }

    [Fact]
    public async Task WikiRead_PageNotFound_ReturnsError()
    {
        var mockKnowledge = new Mock<IKnowledgeService>();
        mockKnowledge.Setup(k => k.GetPageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgePage?)null);

        var tool = WikiReadTool.Create(BuildScopeFactory(mockKnowledge.Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["key"] = "missing" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task WikiRead_MissingKey_ReturnsError()
    {
        var tool = WikiReadTool.Create(BuildScopeFactory(new Mock<IKnowledgeService>().Object));
        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("required");
    }

    // WikiWriteTool

    [Fact]
    public async Task WikiWrite_SavesPage()
    {
        var mockKnowledge = new Mock<IKnowledgeService>();
        mockKnowledge.Setup(k => k.PutPageAsync("wiki/test", "# Content", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tool = WikiWriteTool.Create(BuildScopeFactory(mockKnowledge.Object));
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
        var tool = WikiWriteTool.Create(BuildScopeFactory(new Mock<IKnowledgeService>().Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["content"] = "content" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("key");
    }

    [Fact]
    public async Task WikiWrite_MissingContent_ReturnsError()
    {
        var tool = WikiWriteTool.Create(BuildScopeFactory(new Mock<IKnowledgeService>().Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["key"] = "wiki/test" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("content");
    }
}

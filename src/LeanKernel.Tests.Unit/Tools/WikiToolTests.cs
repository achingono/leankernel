using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace LeanKernel.Tests.Unit.Tools;

public class WikiToolTests
{
    [Fact]
    public async Task WikiSearchTool_returns_validation_error_when_query_is_missing()
    {
        var tool = WikiSearchTool.Create(CreateScopeFactory(Mock.Of<IKnowledgeService>()));

        var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Query is required");
    }

    [Fact]
    public async Task WikiSearchTool_reads_json_arguments_and_resolves_knowledge_service_per_execution()
    {
        var knowledge = new Mock<IKnowledgeService>();
        knowledge
            .Setup(mock => mock.SearchAsync("docs", 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new RetrievalCandidate
                {
                    Key = "wiki/docs",
                    Content = "Documentation page",
                    Source = "wiki",
                    Score = 0.91,
                    TokenCount = 42
                }
            });

        var tool = WikiSearchTool.Create(CreateScopeFactory(knowledge.Object));
        var arguments = new Dictionary<string, object?>
        {
            ["query"] = JsonSerializer.SerializeToElement("docs"),
            ["max_results"] = JsonSerializer.SerializeToElement(7)
        };

        var result = await tool.Handler!(arguments, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("wiki/docs");
        knowledge.Verify(mock => mock.SearchAsync("docs", 7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WikiSearchTool_uses_default_max_results_when_the_argument_is_not_positive()
    {
        var knowledge = new Mock<IKnowledgeService>();
        knowledge
            .Setup(mock => mock.SearchAsync("docs", 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RetrievalCandidate>());

        var tool = WikiSearchTool.Create(CreateScopeFactory(knowledge.Object));
        var arguments = new Dictionary<string, object?>
        {
            ["query"] = "docs",
            ["max_results"] = 0
        };

        var result = await tool.Handler!(arguments, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("No results found.");
        knowledge.Verify(mock => mock.SearchAsync("docs", 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WikiReadTool_returns_page_content_when_found()
    {
        var knowledge = new Mock<IKnowledgeService>();
        knowledge
            .Setup(mock => mock.GetPageAsync("wiki/docs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgePage
            {
                Key = "wiki/docs",
                Content = "# Docs"
            });

        var tool = WikiReadTool.Create(CreateScopeFactory(knowledge.Object));
        var arguments = new Dictionary<string, object?> { ["key"] = "wiki/docs" };

        var result = await tool.Handler!(arguments, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("# Docs");
    }

    [Fact]
    public async Task WikiReadTool_returns_not_found_when_page_is_missing()
    {
        var knowledge = new Mock<IKnowledgeService>();
        knowledge
            .Setup(mock => mock.GetPageAsync("wiki/missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgePage?)null);

        var tool = WikiReadTool.Create(CreateScopeFactory(knowledge.Object));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["key"] = "wiki/missing" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Page 'wiki/missing' not found");
    }

    [Fact]
    public async Task WikiWriteTool_requires_non_blank_content()
    {
        var tool = WikiWriteTool.Create(CreateScopeFactory(Mock.Of<IKnowledgeService>()));

        var result = await tool.Handler!(
            new Dictionary<string, object?> { ["key"] = "wiki/docs", ["content"] = "  " },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Content is required");
    }

    [Fact]
    public async Task WikiWriteTool_persists_content_through_the_knowledge_service()
    {
        var knowledge = new Mock<IKnowledgeService>();
        knowledge
            .Setup(mock => mock.PutPageAsync("wiki/docs", "# Docs", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tool = WikiWriteTool.Create(CreateScopeFactory(knowledge.Object));
        var arguments = new Dictionary<string, object?>
        {
            ["key"] = "wiki/docs",
            ["content"] = "# Docs"
        };

        var result = await tool.Handler!(arguments, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("Page 'wiki/docs' updated successfully.");
        knowledge.Verify(mock => mock.PutPageAsync("wiki/docs", "# Docs", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static IServiceScopeFactory CreateScopeFactory(IKnowledgeService knowledgeService)
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider
            .Setup(mock => mock.GetService(typeof(IKnowledgeService)))
            .Returns(knowledgeService);

        return new TestServiceScopeFactory(serviceProvider.Object);
    }

    private sealed class TestServiceScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public TestServiceScopeFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IServiceScope CreateScope()
        {
            return new TestServiceScope(_serviceProvider);
        }
    }

    private sealed class TestServiceScope : IServiceScope
    {
        public TestServiceScope(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IServiceProvider ServiceProvider { get; }

        public void Dispose()
        {
        }
    }
}

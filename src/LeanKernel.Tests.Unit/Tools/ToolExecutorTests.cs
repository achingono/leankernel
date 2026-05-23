using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools;
using Microsoft.Extensions.Logging;
using Moq;

namespace LeanKernel.Tests.Unit.Tools;

public class ToolExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_returns_not_found_when_tool_is_missing()
    {
        var registry = new Mock<IToolRegistry>();
        registry.Setup(mock => mock.GetTool("missing")).Returns((ToolDefinition?)null);
        var executor = new ToolExecutor(registry.Object, Mock.Of<ILogger<ToolExecutor>>());

        var result = await executor.ExecuteAsync("missing", new Dictionary<string, object?>());

        result.Success.Should().BeFalse();
        result.ToolName.Should().Be("missing");
        result.Error.Should().Be("Tool 'missing' not found");
    }

    [Fact]
    public async Task ExecuteAsync_returns_error_when_tool_has_no_handler()
    {
        var registry = new Mock<IToolRegistry>();
        registry.Setup(mock => mock.GetTool("wiki_search")).Returns(new ToolDefinition
        {
            Name = "wiki_search",
            Description = "Search the wiki"
        });
        var executor = new ToolExecutor(registry.Object, Mock.Of<ILogger<ToolExecutor>>());

        var result = await executor.ExecuteAsync("wiki_search", new Dictionary<string, object?>());

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Tool 'wiki_search' has no execution handler");
    }

    [Fact]
    public async Task ExecuteAsync_returns_handler_result_when_execution_succeeds()
    {
        var registry = new Mock<IToolRegistry>();
        registry.Setup(mock => mock.GetTool("wiki_search")).Returns(new ToolDefinition
        {
            Name = "wiki_search",
            Description = "Search the wiki",
            Handler = (arguments, _) => Task.FromResult(new ToolResult
            {
                ToolName = "wiki_search",
                Success = true,
                Output = arguments["query"]?.ToString()
            })
        });
        var executor = new ToolExecutor(registry.Object, Mock.Of<ILogger<ToolExecutor>>());
        var arguments = new Dictionary<string, object?> { ["query"] = "docs" };

        var result = await executor.ExecuteAsync("wiki_search", arguments);

        result.Success.Should().BeTrue();
        result.Output.Should().Be("docs");
    }

    [Fact]
    public async Task ExecuteAsync_translates_handler_exceptions_into_failed_results()
    {
        var registry = new Mock<IToolRegistry>();
        registry.Setup(mock => mock.GetTool("wiki_search")).Returns(new ToolDefinition
        {
            Name = "wiki_search",
            Description = "Search the wiki",
            Handler = (_, _) => throw new InvalidOperationException("boom")
        });
        var executor = new ToolExecutor(registry.Object, Mock.Of<ILogger<ToolExecutor>>());

        var result = await executor.ExecuteAsync("wiki_search", new Dictionary<string, object?>());

        result.Success.Should().BeFalse();
        result.ToolName.Should().Be("wiki_search");
        result.Error.Should().Be("boom");
    }

    [Fact]
    public async Task ExecuteAsync_propagates_cancellation()
    {
        var registry = new Mock<IToolRegistry>();
        registry.Setup(mock => mock.GetTool("wiki_search")).Returns(new ToolDefinition
        {
            Name = "wiki_search",
            Description = "Search the wiki",
            Handler = (_, cancellationToken) => Task.FromCanceled<ToolResult>(cancellationToken)
        });
        var executor = new ToolExecutor(registry.Object, Mock.Of<ILogger<ToolExecutor>>());
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        Func<Task> act = () => executor.ExecuteAsync(
            "wiki_search",
            new Dictionary<string, object?>(),
            cancellationTokenSource.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

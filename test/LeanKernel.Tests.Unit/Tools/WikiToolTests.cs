using FluentAssertions;

using LeanKernel.Logic.Providers;
using LeanKernel.Logic.Tools.Memory;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class WikiToolTests
{
    private IServiceScopeFactory BuildScopeFactory(IMemoryClient memoryClient)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryClient>(memoryClient);
        services.AddSingleton<IPermit>(Mock.Of<IPermit>(p =>
            p.TenantId == Guid.Empty && p.PersonId == Guid.Empty && p.ChannelId == Guid.Empty));
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

    private static MemoryItem MakeMemoryItem(string key, string text, double score = 1.0)
    {
        return new MemoryItem { Key = key, Text = text, Score = score, Source = "gbrain", ChannelId = Guid.NewGuid(), ScopeRelativeKey = key };
    }

    // MemorySearchTool
    [Fact]
    public async Task WikiSearch_ReturnsResults()
    {
        var mockClient = new Mock<IMemoryClient>();
        mockClient.Setup(c => c.SearchMemoriesAsync(It.IsAny<MemoryScope>(), "test", 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeMemoryItem("page/1", "Content", 0.9)]);

        var tool = MemorySearchTool.Create(BuildScopeFactory(mockClient.Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["query"] = "test" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("page/1");
    }

    [Fact]
    public async Task WikiSearch_MissingQuery_ReturnsError()
    {
        var tool = MemorySearchTool.Create(BuildScopeFactory(Mock.Of<IMemoryClient>()));
        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("required");
    }

    [Fact]
    public async Task WikiSearch_Exception_ReturnsError()
    {
        var mockClient = new Mock<IMemoryClient>();
        mockClient.Setup(c => c.SearchMemoriesAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("GBrain error"));

        var tool = MemorySearchTool.Create(BuildScopeFactory(mockClient.Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["query"] = "test" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("GBrain error");
    }

    [Fact]
    public void WikiSearch_Properties_AreCorrect()
    {
        var tool = MemorySearchTool.Create(BuildScopeFactory(Mock.Of<IMemoryClient>()));
        tool.Name.Should().Be("memory_search");
        tool.Category.Should().Be("knowledge");
    }

    // MemoryReadTool
    [Fact]
    public async Task WikiRead_ReturnsPage()
    {
        var mockClient = new Mock<IMemoryClient>();
        mockClient.Setup(c => c.GetMemoryAsync(It.IsAny<MemoryScope>(), "docs/readme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeMemoryItem("docs/readme", "# README"));

        var tool = MemoryReadTool.Create(BuildScopeFactory(mockClient.Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["key"] = "docs/readme" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("README");
    }

    [Fact]
    public async Task WikiRead_PageNotFound_ReturnsError()
    {
        var mockClient = new Mock<IMemoryClient>();
        mockClient.Setup(c => c.GetMemoryAsync(It.IsAny<MemoryScope>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MemoryItem?)null);

        var tool = MemoryReadTool.Create(BuildScopeFactory(mockClient.Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["key"] = "missing" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task WikiRead_MissingKey_ReturnsError()
    {
        var tool = MemoryReadTool.Create(BuildScopeFactory(Mock.Of<IMemoryClient>()));
        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("required");
    }

    // MemoryWriteTool
    [Fact]
    public async Task WikiWrite_SavesPage()
    {
        var mockClient = new Mock<IMemoryClient>();

        var tool = MemoryWriteTool.Create(BuildScopeFactory(mockClient.Object));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["key"] = "wiki/test", ["content"] = "# Content" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("saved");
        mockClient.Verify(c => c.SaveMemoryAsync(It.IsAny<MemoryScope>(), "wiki/test", "# Content", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WikiWrite_MissingKey_ReturnsError()
    {
        var tool = MemoryWriteTool.Create(BuildScopeFactory(Mock.Of<IMemoryClient>()));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["content"] = "content" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("key");
    }

    [Fact]
    public async Task WikiWrite_MissingContent_ReturnsError()
    {
        var tool = MemoryWriteTool.Create(BuildScopeFactory(Mock.Of<IMemoryClient>()));
        var result = await tool.Handler(
            new Dictionary<string, object?> { ["key"] = "wiki/test" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("content");
    }
}
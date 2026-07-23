using System.Text.Json;

using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Logic.Providers;
using LeanKernel.Logic.Tools.DocumentIngestion;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.DocumentIngestion;

public sealed class DocumentSearchToolTests
{
    private readonly Mock<IDocumentStoreClient> _storeMock;
    private readonly Mock<IPermit> _permitMock;
    private readonly Mock<IChannelMemoryPolicyResolver> _policyResolverMock;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _personId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();

    public DocumentSearchToolTests()
    {
        _storeMock = new Mock<IDocumentStoreClient>();
        _permitMock = new Mock<IPermit>();
        _policyResolverMock = new Mock<IChannelMemoryPolicyResolver>();

        _permitMock.Setup(p => p.TenantId).Returns(_tenantId);
        _permitMock.Setup(p => p.UserId).Returns(_userId);
        _permitMock.Setup(p => p.PersonId).Returns(_personId);
        _permitMock.Setup(p => p.ChannelId).Returns(_channelId);

        _policyResolverMock
            .Setup(r => r.ResolveAsync(_tenantId, _channelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelMemoryPolicyResolution
            {
                TenantId = _tenantId,
                ChannelId = _channelId,
                ReadableChannelIds = new[] { _channelId },
                MutuallyVisibleChannelIds = new[] { _channelId },
            });

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped(_ => _storeMock.Object);
        serviceCollection.AddScoped(_ => _permitMock.Object);
        serviceCollection.AddScoped(_ => _policyResolverMock.Object);
        _scopeFactory = serviceCollection.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public void Create_ThrowsOnNullScopeFactory()
    {
        var act = () => DocumentSearchTool.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Handler_EmptyQuery_ReturnsError()
    {
        var tool = DocumentSearchTool.Create(_scopeFactory);
        var args = new Dictionary<string, object?> { ["query"] = string.Empty };

        var result = await tool.Handler!(args, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("query is required");
    }

    [Fact]
    public async Task Handler_WhitespaceQuery_ReturnsError()
    {
        var tool = DocumentSearchTool.Create(_scopeFactory);
        var args = new Dictionary<string, object?> { ["query"] = "   " };

        var result = await tool.Handler!(args, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("query is required");
    }

    [Fact]
    public async Task Handler_ValidQuery_CallsStoreSearch()
    {
        var hits = new List<DocumentSearchHit>
        {
            new("fp1", "doc1.txt", "text/plain", "sample text", 0.95, DateTime.UtcNow),
        };

        _storeMock
            .Setup(s => s.SearchAsync(
                It.IsAny<DocumentScopeContext>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(hits);

        var tool = DocumentSearchTool.Create(_scopeFactory);
        var args = new Dictionary<string, object?> { ["query"] = "test query" };

        var result = await tool.Handler!(args, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("doc1.txt");
    }

    [Fact]
    public async Task Handler_WithChannelIds_FiltersAuthorized()
    {
        var anotherChannel = Guid.NewGuid();
        _policyResolverMock
            .Setup(r => r.ResolveAsync(_tenantId, _channelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelMemoryPolicyResolution
            {
                TenantId = _tenantId,
                ChannelId = _channelId,
                ReadableChannelIds = new[] { anotherChannel },
                MutuallyVisibleChannelIds = new[] { anotherChannel },
            });

        var tool = DocumentSearchTool.Create(_scopeFactory);
        var args = new Dictionary<string, object?>
        {
            ["query"] = "test",
            ["channelIds"] = $"{anotherChannel}"
        };

        var result = await tool.Handler!(args, CancellationToken.None);

        result.Success.Should().BeTrue();
        _storeMock.Verify(s => s.SearchAsync(
            It.IsAny<DocumentScopeContext>(),
            "test",
            It.Is<IReadOnlyList<Guid>>(l => l.Contains(anotherChannel)),
            10,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handler_WithUnauthorizedChannel_ReturnsError()
    {
        var forbiddenChannel = Guid.NewGuid();
        _policyResolverMock
            .Setup(r => r.ResolveAsync(_tenantId, _channelId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelMemoryPolicyResolution
            {
                TenantId = _tenantId,
                ChannelId = _channelId,
                ReadableChannelIds = new[] { _channelId },
                MutuallyVisibleChannelIds = new[] { _channelId },
            });

        var tool = DocumentSearchTool.Create(_scopeFactory);
        var args = new Dictionary<string, object?>
        {
            ["query"] = "test",
            ["channelIds"] = $"{forbiddenChannel}"
        };

        var result = await tool.Handler!(args, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Not authorized");
    }

    [Fact]
    public async Task Handler_StoreThrows_ReturnsError()
    {
        _storeMock
            .Setup(s => s.SearchAsync(
                It.IsAny<DocumentScopeContext>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store failure"));

        var tool = DocumentSearchTool.Create(_scopeFactory);
        var args = new Dictionary<string, object?> { ["query"] = "test" };

        var result = await tool.Handler!(args, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("store failure");
    }

    [Fact]
    public async Task Handler_MaxResults_DefaultsToTen()
    {
        _storeMock
            .Setup(s => s.SearchAsync(
                It.IsAny<DocumentScopeContext>(),
                "query",
                It.IsAny<IReadOnlyList<Guid>>(),
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DocumentSearchHit>());

        var tool = DocumentSearchTool.Create(_scopeFactory);
        var args = new Dictionary<string, object?> { ["query"] = "query" };

        var result = await tool.Handler!(args, CancellationToken.None);

        result.Success.Should().BeTrue();
        _storeMock.Verify(s => s.SearchAsync(
            It.IsAny<DocumentScopeContext>(),
            "query",
            It.IsAny<IReadOnlyList<Guid>>(),
            10,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handler_MaxResults_CustomValue()
    {
        _storeMock
            .Setup(s => s.SearchAsync(
                It.IsAny<DocumentScopeContext>(),
                "query",
                It.IsAny<IReadOnlyList<Guid>>(),
                5,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DocumentSearchHit>());

        var tool = DocumentSearchTool.Create(_scopeFactory);
        var args = new Dictionary<string, object?> { ["query"] = "query", ["maxResults"] = 5 };

        var result = await tool.Handler!(args, CancellationToken.None);

        result.Success.Should().BeTrue();
        _storeMock.Verify(s => s.SearchAsync(
            It.IsAny<DocumentScopeContext>(),
            "query",
            It.IsAny<IReadOnlyList<Guid>>(),
            5,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

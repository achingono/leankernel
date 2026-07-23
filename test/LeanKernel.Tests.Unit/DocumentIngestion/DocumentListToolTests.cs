using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Logic.Providers;
using LeanKernel.Logic.Tools.DocumentIngestion;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.DocumentIngestion;

public sealed class DocumentListToolTests
{
    private readonly Mock<IDocumentStoreClient> _storeMock;
    private readonly Mock<IPermit> _permitMock;
    private readonly Mock<IChannelMemoryPolicyResolver> _policyResolverMock;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _personId = Guid.NewGuid();
    private readonly Guid _channelId = Guid.NewGuid();

    public DocumentListToolTests()
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
        var act = () => DocumentListTool.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Handler_NoChannelIds_ReturnsAllReadable()
    {
        var entries = new List<DocumentCatalogEntry>
        {
            new("fp1", "doc1.txt", "text/plain", "text", _tenantId, _userId, _personId, _channelId, DocumentAvailabilityScope.Channel, DateTime.UtcNow),
        };

        _storeMock
            .Setup(s => s.ListAsync(
                It.IsAny<DocumentScopeContext>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                50,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        var tool = DocumentListTool.Create(_scopeFactory);
        var args = new Dictionary<string, object?>();

        var result = await tool.Handler!(args, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("doc1.txt");
    }

    [Fact]
    public async Task Handler_WithAuthorizedChannelIds_Filters()
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

        _storeMock
            .Setup(s => s.ListAsync(
                It.IsAny<DocumentScopeContext>(),
                It.Is<IReadOnlyList<Guid>>(l => l.Contains(anotherChannel)),
                50,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DocumentCatalogEntry>());

        var tool = DocumentListTool.Create(_scopeFactory);
        var args = new Dictionary<string, object?> { ["channelIds"] = $"{anotherChannel}" };

        var result = await tool.Handler!(args, CancellationToken.None);

        result.Success.Should().BeTrue();
        _storeMock.Verify(s => s.ListAsync(
            It.IsAny<DocumentScopeContext>(),
            It.Is<IReadOnlyList<Guid>>(l => l.Contains(anotherChannel)),
            50,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handler_WithUnauthorizedChannel_ReturnsError()
    {
        var forbiddenChannel = Guid.NewGuid();

        var tool = DocumentListTool.Create(_scopeFactory);
        var args = new Dictionary<string, object?> { ["channelIds"] = $"{forbiddenChannel}" };

        var result = await tool.Handler!(args, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Not authorized");
    }

    [Fact]
    public async Task Handler_StoreThrows_ReturnsError()
    {
        _storeMock
            .Setup(s => s.ListAsync(
                It.IsAny<DocumentScopeContext>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                50,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("store error"));

        var tool = DocumentListTool.Create(_scopeFactory);
        var args = new Dictionary<string, object?>();

        var result = await tool.Handler!(args, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("store error");
    }

    [Fact]
    public async Task Handler_Limit_DefaultsToFifty()
    {
        _storeMock
            .Setup(s => s.ListAsync(
                It.IsAny<DocumentScopeContext>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                50,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DocumentCatalogEntry>());

        var tool = DocumentListTool.Create(_scopeFactory);
        var args = new Dictionary<string, object?>();

        var result = await tool.Handler!(args, CancellationToken.None);

        result.Success.Should().BeTrue();
        _storeMock.Verify(s => s.ListAsync(
            It.IsAny<DocumentScopeContext>(),
            It.IsAny<IReadOnlyList<Guid>>(),
            50,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handler_CustomLimit()
    {
        _storeMock
            .Setup(s => s.ListAsync(
                It.IsAny<DocumentScopeContext>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DocumentCatalogEntry>());

        var tool = DocumentListTool.Create(_scopeFactory);
        var args = new Dictionary<string, object?> { ["limit"] = 10 };

        var result = await tool.Handler!(args, CancellationToken.None);

        result.Success.Should().BeTrue();
        _storeMock.Verify(s => s.ListAsync(
            It.IsAny<DocumentScopeContext>(),
            It.IsAny<IReadOnlyList<Guid>>(),
            10,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

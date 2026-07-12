using FluentAssertions;
using LeanKernel.Logic.Providers;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Providers;

public class MemoryProviderTests
{
    private static IPermit CreatePermit(
        Guid? tenantId = null,
        Guid? userId = null,
        Guid? channelId = null)
    {
        var mock = new Mock<IPermit>();
        mock.Setup(p => p.TenantId).Returns(tenantId ?? Guid.NewGuid());
        mock.Setup(p => p.UserId).Returns(userId ?? Guid.NewGuid());
        mock.Setup(p => p.ChannelId).Returns(channelId ?? Guid.NewGuid());
        mock.Setup(p => p.IsAuthenticated).Returns(true);
        return mock.Object;
    }

    [Fact]
    public async Task StubMemoryClient_SearchMemories_ReturnsEmptyResults()
    {
        var client = new StubMemoryClient();
        var scope = new MemoryScope
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid()
        };

        var results = await client.SearchMemoriesAsync(scope, "test query");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task StubMemoryClient_SaveMemory_Completes()
    {
        var client = new StubMemoryClient();
        var scope = new MemoryScope
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid()
        };

        await client.SaveMemoryAsync(scope, "key", "content");
    }

    [Fact]
    public void MemoryScope_Properties_SetCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var scope = new MemoryScope
        {
            TenantId = tenantId,
            UserId = userId,
            ChannelId = channelId,
            Namespace = "test-ns"
        };

        scope.TenantId.Should().Be(tenantId);
        scope.UserId.Should().Be(userId);
        scope.ChannelId.Should().Be(channelId);
        scope.Namespace.Should().Be("test-ns");
    }

    [Fact]
    public void MemoryItem_Properties_SetCorrectly()
    {
        var item = new MemoryItem
        {
            Key = "mem-key-1",
            Text = "memory text",
            Score = 0.95,
            Source = "gbrain"
        };

        item.Key.Should().Be("mem-key-1");
        item.Text.Should().Be("memory text");
        item.Score.Should().Be(0.95);
        item.Source.Should().Be("gbrain");
    }

    [Fact]
    public void MemoryProvider_CanBeConstructed()
    {
        var memoryClient = new Mock<IMemoryClient>();
        var permit = CreatePermit();

        var act = () => new MemoryProvider(memoryClient.Object, permit);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task StubMemoryClient_Concurrent_SearchMemories_IsThreadSafe()
    {
        var client = new StubMemoryClient();
        var scope = new MemoryScope
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid()
        };

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => client.SearchMemoriesAsync(scope, "query"))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().AllBeEquivalentTo(Array.Empty<MemoryItem>());
    }
}

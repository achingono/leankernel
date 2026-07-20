using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Gateway.Memory;
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
        Guid? personId = null,
        Guid? channelId = null)
    {
        return new MemoryScope
        {
            TenantId = tenantId ?? Guid.NewGuid(),
            PersonId = personId ?? Guid.NewGuid(),
            ChannelId = channelId ?? Guid.NewGuid()
        };
    }

    private static Mock<IChannelMemoryPolicyResolver> CreatePolicyResolver(Guid? channelId = null)
    {
        var resolver = new Mock<IChannelMemoryPolicyResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, Guid sourceChannelId, CancellationToken _) => new ChannelMemoryPolicyResolution
            {
                TenantId = tenantId,
                ChannelId = sourceChannelId,
                ReadableChannelIds = [channelId ?? sourceChannelId],
                MutuallyVisibleChannelIds = [channelId ?? sourceChannelId]
            });
        return resolver;
    }

    /// <summary>
    /// Verifies the constructor rejects a missing MCP client.
    /// </summary>
    [Fact]
    public void Constructor_NullClient_Throws()
    {
        var act = () => new GBrainMemoryClient(
            null!,
            Mock.Of<IChannelMemoryPolicyResolver>(),
            Mock.Of<ILogger<GBrainMemoryClient>>());

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Verifies the constructor rejects a missing logger.
    /// </summary>
    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var mockClient = new Mock<IGBrainMcpClient>();

        var act = () => new GBrainMemoryClient(
            mockClient.Object,
            Mock.Of<IChannelMemoryPolicyResolver>(),
            null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Verifies search failures are handled by returning no memories.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task SearchMemoriesAsync_WhenClientThrows_ReturnsEmpty()
    {
        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GBrainException("service unavailable"));

        var client = new GBrainMemoryClient(mockClient.Object, CreatePolicyResolver().Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var scope = CreateScope();

        var results = await client.SearchMemoriesAsync(scope, "test query");

        results.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies null search results are treated as empty.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task SearchMemoriesAsync_NullResult_ReturnsEmpty()
    {
        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Text.Json.JsonElement?)null);

        var client = new GBrainMemoryClient(mockClient.Object, CreatePolicyResolver().Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var scope = CreateScope();

        var results = await client.SearchMemoriesAsync(scope, "test query");

        results.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies save failures are propagated to callers.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task SaveMemoryAsync_WhenClientThrows_PropagatesException()
    {
        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GBrainException("write failed"));

        var client = new GBrainMemoryClient(mockClient.Object, CreatePolicyResolver().Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var scope = CreateScope();

        var act = async () => await client.SaveMemoryAsync(scope, "key", "content");

        await act.Should().ThrowAsync<GBrainException>();
    }

    /// <summary>
    /// Verifies successful saves invoke the put page tool.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task SaveMemoryAsync_Success_CallsPutPage()
    {
        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Text.Json.JsonElement?)null);

        var client = new GBrainMemoryClient(mockClient.Object, CreatePolicyResolver().Object, Mock.Of<ILogger<GBrainMemoryClient>>());
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
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task SearchMemoriesAsync_Success_CallsSearch()
    {
        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Text.Json.JsonElement?)null);

        var client = new GBrainMemoryClient(mockClient.Object, CreatePolicyResolver().Object, Mock.Of<ILogger<GBrainMemoryClient>>());
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
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task SaveMemoryAsync_CallsPutPageWithCorrectSlug()
    {
        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((System.Text.Json.JsonElement?)null);

        var client = new GBrainMemoryClient(mockClient.Object, CreatePolicyResolver().Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var scope = CreateScope(
            tenantId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            personId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
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

        var client = new GBrainMemoryClient(mockClient.Object, CreatePolicyResolver().Object, Mock.Of<ILogger<GBrainMemoryClient>>());
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

        var client = new GBrainMemoryClient(mockClient.Object, CreatePolicyResolver().Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var scope = CreateScope();

        var results = await client.SearchMemoriesAsync(scope, "test");

        results.Should().HaveCount(1);
        results[0].Key.Should().Be("a");
        results[0].Source.Should().Be("gbrain");
    }

    [Fact]
    public async Task SearchMemoriesAsync_ArrayFormat_UsesChunkTextFallbackWhenCompiledTruthMissing()
    {
        var json = System.Text.Json.JsonDocument.Parse(
            """[{"slug":"a","chunk_text":"chunk content","score":0.8}]""")
            .RootElement.Clone();

        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync("search", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var client = new GBrainMemoryClient(mockClient.Object, CreatePolicyResolver().Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var scope = CreateScope();

        var results = await client.SearchMemoriesAsync(scope, "test");

        results.Should().HaveCount(1);
        results[0].Text.Should().Be("chunk content");
    }

    /// <summary>
    /// C3: Search must use a namespace derived from TenantId/PersonId/ChannelId.
    /// Ensures search and save use the same identity-scoped namespace for correct recall.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task SearchMemoriesAsync_UsesNamespaceDerivedFromScopeIdentity()
    {
        string? capturedNamespace = null;

        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync("search", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?, CancellationToken>((_, args, _) =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(args);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                doc.RootElement.TryGetProperty("namespace_name", out var ns);
                capturedNamespace = ns.GetString();
            })
            .ReturnsAsync((System.Text.Json.JsonElement?)null);

        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var personId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var channelId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var scope = CreateScope(tenantId, personId, channelId);

        var client = new GBrainMemoryClient(mockClient.Object, CreatePolicyResolver(channelId).Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        await client.SearchMemoriesAsync(scope, "test query");

        capturedNamespace.Should().Be(
            $"memory/{tenantId}/{personId}/{channelId}",
            because: "C3: search must use the same namespace as save to prevent cross-tenant recall");
    }

    /// <summary>
    /// C3: Memory saved under scope A must use a different namespace than memory searched under scope B.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task SearchMemoriesAsync_DifferentScopes_UseDifferentNamespaces()
    {
        var saveSlug = null as string;
        var searchNamespace = null as string;

        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync("put_page", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?, CancellationToken>((_, args, _) =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(args);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                doc.RootElement.TryGetProperty("slug", out var s);
                saveSlug = s.GetString();
            })
            .ReturnsAsync((System.Text.Json.JsonElement?)null);

        mockClient
            .Setup(c => c.CallToolAsync("search", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?, CancellationToken>((_, args, _) =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(args);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                doc.RootElement.TryGetProperty("namespace_name", out var ns);
                searchNamespace = ns.GetString();
            })
            .ReturnsAsync((System.Text.Json.JsonElement?)null);

        var scopeA = CreateScope();
        var scopeB = CreateScope(); // Different random GUIDs

        var client = new GBrainMemoryClient(mockClient.Object, CreatePolicyResolver(scopeB.ChannelId).Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        await client.SaveMemoryAsync(scopeA, "key", "content");
        await client.SearchMemoriesAsync(scopeB, "query");

        saveSlug.Should().StartWith($"memory/{scopeA.TenantId}",
            because: "save must namespace under scope A's tenant");
        searchNamespace.Should().StartWith($"memory/{scopeB.TenantId}",
            because: "search must namespace under scope B's tenant");
        searchNamespace.Should().NotBe(saveSlug![..searchNamespace!.Length],
            because: "cross-scope recall must be impossible");
    }

    [Fact]
    public async Task SearchMemoriesAsync_PolicyFanOut_ReturnsChannelScopedResults()
    {
        var channelA = Guid.NewGuid();
        var channelB = Guid.NewGuid();
        var scope = CreateScope(channelId: channelA);

        var policyResolver = new Mock<IChannelMemoryPolicyResolver>();
        policyResolver
            .Setup(r => r.ResolveAsync(scope.TenantId, channelA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChannelMemoryPolicyResolution
            {
                TenantId = scope.TenantId,
                ChannelId = channelA,
                ReadableChannelIds = [channelA, channelB],
                MutuallyVisibleChannelIds = [channelA, channelB]
            });

        var first = System.Text.Json.JsonDocument.Parse(
            $$"""[{"slug":"memory/{{scope.TenantId}}/{{scope.PersonId}}/{{channelA}}/facts/what/jane/x","compiled_truth":"A","score":0.4}]""")
            .RootElement.Clone();
        var second = System.Text.Json.JsonDocument.Parse(
            $$"""[{"slug":"memory/{{scope.TenantId}}/{{scope.PersonId}}/{{channelB}}/facts/what/jane/x","compiled_truth":"B","score":0.9}]""")
            .RootElement.Clone();

        var callCount = 0;
        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync("search", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1 ? first : second;
            });

        var client = new GBrainMemoryClient(mockClient.Object, policyResolver.Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var result = await client.SearchMemoriesAsync(scope, "jane", 10);

        result.Should().HaveCount(2);
        result.Select(item => item.Text).Should().Contain(["A", "B"]);
    }

    [Fact]
    public async Task SearchMemoriesAsync_ParsesScopedKey_Metadata()
    {
        var scope = CreateScope();
        var json = System.Text.Json.JsonDocument.Parse(
            $$"""[{"slug":"memory/{{scope.TenantId}}/{{scope.PersonId}}/{{scope.ChannelId}}/facts/who/jane/x1","compiled_truth":"content","score":0.8}]""")
            .RootElement.Clone();

        var mockClient = new Mock<IGBrainMcpClient>();
        mockClient
            .Setup(c => c.CallToolAsync("search", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var client = new GBrainMemoryClient(mockClient.Object, CreatePolicyResolver(scope.ChannelId).Object, Mock.Of<ILogger<GBrainMemoryClient>>());
        var result = await client.SearchMemoriesAsync(scope, "jane", 10);

        result.Should().ContainSingle();
        result[0].ChannelId.Should().Be(scope.ChannelId);
        result[0].ScopeRelativeKey.Should().Be("facts/who/jane/x1");
    }
}
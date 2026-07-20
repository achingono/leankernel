using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Memory;
using LeanKernel.Logic.Providers;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Providers;

/// <summary>
/// Covers basic contracts and construction for memory provider types.
/// </summary>
public class MemoryProviderTests
{
    /// <summary>
    /// Creates a permit stub with generated identifiers unless provided.
    /// </summary>
    private static IPermit CreatePermit(
        Guid? tenantId = null,
        Guid? userId = null,
        Guid? personId = null,
        Guid? channelId = null)
    {
        var mock = new Mock<IPermit>();
        mock.Setup(p => p.TenantId).Returns(tenantId ?? Guid.NewGuid());
        mock.Setup(p => p.UserId).Returns(userId ?? Guid.NewGuid());
        mock.Setup(p => p.PersonId).Returns(personId ?? userId ?? Guid.NewGuid());
        mock.Setup(p => p.ChannelId).Returns(channelId ?? Guid.NewGuid());
        mock.Setup(p => p.IsAuthenticated).Returns(true);
        return mock.Object;
    }

    /// <summary>
    /// Verifies the stub memory client returns no search results.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StubMemoryClient_SearchMemories_ReturnsEmptyResults()
    {
        var client = new StubMemoryClient();
        var scope = new MemoryScope
        {
            TenantId = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid()
        };

        var results = await client.SearchMemoriesAsync(scope, "test query");

        results.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the stub memory client accepts save requests.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StubMemoryClient_SaveMemory_Completes()
    {
        var client = new StubMemoryClient();
        var scope = new MemoryScope
        {
            TenantId = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid()
        };

        var act = async () => await client.SaveMemoryAsync(scope, "key", "content");

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Verifies memory scope properties retain assigned values.
    /// </summary>
    [Fact]
    public void MemoryScope_Properties_SetCorrectly()
    {
        var tenantId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var scope = new MemoryScope
        {
            TenantId = tenantId,
            PersonId = personId,
            ChannelId = channelId,
            SearchChannelIds = [channelId]
        };

        scope.TenantId.Should().Be(tenantId);
        scope.PersonId.Should().Be(personId);
        scope.ChannelId.Should().Be(channelId);
        scope.SearchChannelIds.Should().ContainSingle().Which.Should().Be(channelId);
    }

    /// <summary>
    /// Verifies memory item properties retain assigned values.
    /// </summary>
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

    /// <summary>
    /// Verifies the memory provider can be constructed with valid dependencies.
    /// </summary>
    [Fact]
    public void MemoryProvider_CanBeConstructed()
    {
        var memoryClient = new Mock<IMemoryClient>();
        var permit = CreatePermit();
        var parser = new MemoryPageParser();
        var renderer = new MemoryPageRenderer();
        var reasoningModel = new FakeReasoningModel(enabled: false);
        var classifier = new MemoryDimensionClassifier(reasoningModel);
        var linker = new MemoryPageLinker();
        var graph = new MemoryGraphReasoner(reasoningModel, NullLogger<MemoryGraphReasoner>.Instance);
        var repair = new MemoryFieldRepairService(reasoningModel);
        var keyBuilder = new MemoryPageKeyBuilder();
        var normalizer = new MemoryPageNormalizer(classifier, linker, graph, repair, renderer, keyBuilder);
        var factExtraction = new FactExtractionService(
            new FakeChatClient(),
            Options.Create(new FactExtractionSettings()),
            renderer);

        var act = () => new MemoryProvider(
            memoryClient.Object,
            permit,
            Mock.Of<IChannelMemoryPolicyResolver>(),
            parser,
            renderer,
            normalizer,
            factExtraction,
            TimeProvider.System,
            NullLogger<MemoryProvider>.Instance);

        act.Should().NotThrow();
    }

    /// <summary>
    /// Verifies the stub memory client supports concurrent searches.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StubMemoryClient_Concurrent_SearchMemories_IsThreadSafe()
    {
        var client = new StubMemoryClient();
        var scope = new MemoryScope
        {
            TenantId = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid()
        };

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => client.SearchMemoriesAsync(scope, "query"))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().AllBeEquivalentTo(Array.Empty<MemoryItem>());
    }
}

/// <summary>
/// Provides a disabled reasoning model for memory provider tests.
/// </summary>
/// <param name="enabled">Whether the model reports itself as enabled.</param>
file sealed class FakeReasoningModel(bool enabled) : IReasoningModel
{
    public bool Enabled => enabled;

    /// <inheritdoc />
    public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, int maxOutputTokens, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }
}

/// <summary>
/// Returns an empty fact array for extraction-related tests.
/// </summary>
file sealed class FakeChatClient : IChatClient
{
    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = new ChatResponse([
            new ChatMessage(ChatRole.Assistant, "[]")
        ]);
        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        return AsyncEnumerable.Empty<ChatResponseUpdate>();
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    /// <inheritdoc />
    public void Dispose() => GC.SuppressFinalize(this);
}
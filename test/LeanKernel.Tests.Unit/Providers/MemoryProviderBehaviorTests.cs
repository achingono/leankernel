using FluentAssertions;
using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Memory;
using LeanKernel.Logic.Providers;
using LeanKernel.Tests.Unit.TestDoubles;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Providers;

#pragma warning disable MAAI001

/// <summary>
/// Covers end-to-end behavior of the memory provider pipeline.
/// </summary>
public class MemoryProviderBehaviorTests
{
    /// <summary>
    /// Verifies no context is added when the request contains no query text.
    /// </summary>
    [Fact]
    public async Task ProvideContext_ReturnsEmpty_WhenNoQueryText()
    {
        var (provider, _) = CreateSut(new InMemoryMemoryClient());
        var invoking = new AIContextProvider.InvokingContext(
            new TestAIAgent(),
            new TestAgentSession(),
            new AIContext { Messages = [] });

        var context = await provider.ProvideForTestAsync(invoking);

        context.Messages.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies compact memory summaries are returned when matches exist.
    /// </summary>
    [Fact]
    public async Task ProvideContext_ReturnsCompactSummaries_WhenMemoriesExist()
    {
        var memory = new InMemoryMemoryClient
        {
            SearchResults =
            [
                new MemoryItem
                {
                    Key = "facts/what/q4/1",
                    Text = "# Learned Fact\n\nJane approved Q4 budget\n\n- Session: s1\n- Turn: t1\n- RecordedAt: 2026-07-10T12:00:00Z",
                    Score = 0.9
                }
            ]
        };
        var (provider, _) = CreateSut(memory);
        var invoking = new AIContextProvider.InvokingContext(
            new TestAIAgent(),
            new TestAgentSession(),
            new AIContext
            {
                Messages = [new ChatMessage(ChatRole.User, "budget")]
            });

        var context = await provider.ProvideForTestAsync(invoking);

        context.Messages.Should().HaveCount(1);
        context.Messages!.First().Text.Should().Contain("dimensions:");
    }

    [Fact]
    public async Task ProvideContext_FallsBackToRawSnippet_WhenParsedFactTextIsEmpty()
    {
        var memory = new InMemoryMemoryClient
        {
            SearchResults =
            [
                new MemoryItem
                {
                    Key = "memory/tenant/person/channel/imported/who/karen-leung",
                    Text = "Karen has shown she won't champion internal advancement; manage distance and focus on exit.",
                    Score = 0.9
                }
            ]
        };

        var (provider, _) = CreateSut(memory);
        var invoking = new AIContextProvider.InvokingContext(
            new TestAIAgent(),
            new TestAgentSession(),
            new AIContext
            {
                Messages = [new ChatMessage(ChatRole.User, "What do you know about Karen?")]
            });

        var context = await provider.ProvideForTestAsync(invoking);

        context.Messages.Should().HaveCount(1);
        context.Messages!.First().Text.Should().Contain("Karen has shown she won't champion internal advancement");
    }

    [Fact]
    public async Task ProvideContext_OverlayPrefersLocalChannelFact()
    {
        var localChannel = Guid.NewGuid();
        var remoteChannel = Guid.NewGuid();

        var localText = """
# Learned Fact

Local channel answer

- Session: s-local
- Turn: t-local
- RecordedAt: 2026-07-11T12:00:00Z
""";

        var remoteText = """
# Learned Fact

Remote channel answer

- Session: s-remote
- Turn: t-remote
- RecordedAt: 2026-07-12T12:00:00Z
""";

        var memory = new InMemoryMemoryClient
        {
            SearchResults =
            [
                new MemoryItem
                {
                    Key = "memory/tenant/person/" + remoteChannel + "/facts/what/jane/x",
                    ScopeRelativeKey = "facts/what/jane/x",
                    ChannelId = remoteChannel,
                    Text = remoteText,
                    Score = 0.9
                },
                new MemoryItem
                {
                    Key = "memory/tenant/person/" + localChannel + "/facts/what/jane/x",
                    ScopeRelativeKey = "facts/what/jane/x",
                    ChannelId = localChannel,
                    Text = localText,
                    Score = 0.4
                }
            ]
        };

        var (provider, _) = CreateSut(memory, channelId: localChannel);
        var invoking = new AIContextProvider.InvokingContext(
            new TestAIAgent(),
            new TestAgentSession(),
            new AIContext
            {
                Messages = [new ChatMessage(ChatRole.User, "what is the fact")]
            });

        var context = await provider.ProvideForTestAsync(invoking);

        context.Messages.Should().HaveCount(1);
        context.Messages!.First().Text.Should().Contain("Local channel answer");
        context.Messages!.First().Text.Should().NotContain("Remote channel answer");
    }

    /// <summary>
    /// Verifies normalized facts are persisted after invocation.
    /// </summary>
    [Fact]
    public async Task StoreContext_PersistsNormalizedFacts()
    {
        var memory = new InMemoryMemoryClient();
        var (provider, session) = CreateSut(memory, extractionResponse: "[\"Jane approved Q4 budget in Seattle\"]");
        session.StateBag.SetValue("chatSessionId", "sess-x");

        var invoked = new AIContextProvider.InvokedContext(
            new TestAIAgent(),
            session,
            [new ChatMessage(ChatRole.User, "what happened?")],
            [new ChatMessage(ChatRole.Assistant, "Jane approved Q4 budget in Seattle")]);

        await provider.StoreForTestAsync(invoked);

        memory.Saved.Should().ContainSingle();
        memory.Saved[0].Key.Should().StartWith("facts/");
        memory.Saved[0].Content.Should().Contain("## 5W1H");
    }

    /// <summary>
    /// Verifies raw fact pages are saved when normalization fails.
    /// </summary>
    [Fact]
    public async Task StoreContext_FallsBackToRawSave_OnPipelineFailure()
    {
        var memory = new InMemoryMemoryClient();
        var (provider, session) = CreateSut(memory, extractionResponse: null, extractionThrows: true);

        var invoked = new AIContextProvider.InvokedContext(
            new TestAIAgent(),
            session,
            [new ChatMessage(ChatRole.User, "u")],
            [new ChatMessage(ChatRole.Assistant, "a")]);

        await provider.StoreForTestAsync(invoked);

        memory.Saved.Should().ContainSingle();
        memory.Saved[0].Key.Should().Contain("fact-fallback");
        memory.Saved[0].Content.Should().Contain("# Learned Fact");
    }

    [Fact]
    public async Task StoreContext_DoesNotThrow_WhenFallbackSaveFails()
    {
        var memory = new InMemoryMemoryClient
        {
            ThrowOnSave = true
        };
        var (provider, session) = CreateSut(memory, extractionResponse: null, extractionThrows: true);

        var invoked = new AIContextProvider.InvokedContext(
            new TestAIAgent(),
            session,
            [new ChatMessage(ChatRole.User, "u")],
            [new ChatMessage(ChatRole.Assistant, "a")]);

        var act = async () => await provider.StoreForTestAsync(invoked);

        await act.Should().NotThrowAsync();
        memory.Saved.Should().BeEmpty();
    }

    /// <summary>
    /// Creates a provider under test with configurable extraction behavior.
    /// </summary>
    private static (TestableMemoryProvider Provider, TestAgentSession Session) CreateSut(
        InMemoryMemoryClient memoryClient,
        string? extractionResponse = "[]",
        bool extractionThrows = false,
        Guid? channelId = null)
    {
        var permit = new Mock<IPermit>();
        permit.SetupGet(p => p.TenantId).Returns(Guid.NewGuid());
        permit.SetupGet(p => p.UserId).Returns(Guid.NewGuid());
        permit.SetupGet(p => p.PersonId).Returns(Guid.NewGuid());
        permit.SetupGet(p => p.ChannelId).Returns(channelId ?? Guid.NewGuid());
        permit.SetupGet(p => p.SessionId).Returns("permit-session");

        var policyResolver = new Mock<IChannelMemoryPolicyResolver>();
        policyResolver
            .Setup(x => x.ResolveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, Guid channelId, CancellationToken _) => new ChannelMemoryPolicyResolution
            {
                TenantId = tenantId,
                ChannelId = channelId,
                ReadableChannelIds = [channelId],
                MutuallyVisibleChannelIds = [channelId]
            });

        var renderer = new MemoryPageRenderer();
        var reasoning = new StubReasoningModel();
        var classifier = new MemoryDimensionClassifier(reasoning);
        var linker = new MemoryPageLinker();
        var graph = new MemoryGraphReasoner(reasoning, NullLogger<MemoryGraphReasoner>.Instance);
        var repair = new MemoryFieldRepairService(reasoning);
        var keyBuilder = new MemoryPageKeyBuilder();
        var normalizer = new MemoryPageNormalizer(classifier, linker, graph, repair, renderer, keyBuilder);

        IChatClient chatClient = extractionThrows
            ? new ThrowingChatClient()
            : new StaticChatClient(extractionResponse ?? "[]");

        var extraction = new FactExtractionService(chatClient, Options.Create(new FactExtractionSettings()), renderer);

        return (
            new TestableMemoryProvider(
                memoryClient,
                permit.Object,
                policyResolver.Object,
                new MemoryPageParser(),
                renderer,
                normalizer,
                extraction,
                TimeProvider.System,
                NullLogger<MemoryProvider>.Instance),
            new TestAgentSession());
    }

    /// <summary>
    /// Exposes protected memory provider hooks for tests.
    /// </summary>
    /// <param name="memoryClient">The memory client to wrap.</param>
    /// <param name="permit">The permit used to resolve memory scope.</param>
    /// <param name="memoryPolicyResolver">The channel memory policy resolver.</param>
    /// <param name="parser">The memory page parser to use.</param>
    /// <param name="renderer">The memory page renderer to use.</param>
    /// <param name="normalizer">The page normalizer to use.</param>
    /// <param name="factExtractionService">The fact extraction service to use.</param>
    /// <param name="timeProvider">The time provider to use.</param>
    /// <param name="logger">The logger to use.</param>
    private sealed class TestableMemoryProvider(
        IMemoryClient memoryClient,
        IPermit permit,
        IChannelMemoryPolicyResolver memoryPolicyResolver,
        MemoryPageParser parser,
        MemoryPageRenderer renderer,
        MemoryPageNormalizer normalizer,
        FactExtractionService factExtractionService,
        TimeProvider timeProvider,
        Microsoft.Extensions.Logging.ILogger<MemoryProvider> logger)
        : MemoryProvider(memoryClient, permit, memoryPolicyResolver, parser, renderer, normalizer, factExtractionService, timeProvider, logger)
    {
        /// <summary>
        /// Invokes context provisioning for tests.
        /// </summary>
        public async Task<AIContext> ProvideForTestAsync(AIContextProvider.InvokingContext context)
            => await ProvideAIContextAsync(context);

        /// <summary>
        /// Invokes context storage for tests.
        /// </summary>
        public async Task StoreForTestAsync(AIContextProvider.InvokedContext context)
            => await StoreAIContextAsync(context);
    }

    /// <summary>
    /// Stores memory operations in memory for assertions.
    /// </summary>
    private sealed class InMemoryMemoryClient : IMemoryClient
    {
        public IReadOnlyList<MemoryItem> SearchResults { get; init; } = [];
        public List<(string Key, string Content)> Saved { get; } = [];
        public bool ThrowOnSave { get; init; }

        /// <inheritdoc />
        public Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(MemoryScope scope, string query, int maxResults = 10, CancellationToken ct = default)
        {
            return Task.FromResult(SearchResults);
        }

        /// <inheritdoc />
        public Task SaveMemoryAsync(MemoryScope scope, string key, string content, CancellationToken ct = default)
        {
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("save failed");
            }

            Saved.Add((key, content));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Provides a reasoning model stub that is always disabled.
    /// </summary>
    private sealed class StubReasoningModel : IReasoningModel
    {
        public bool Enabled => false;

        /// <inheritdoc />
        public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, int maxOutputTokens, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Returns a fixed extraction response from the chat client.
    /// </summary>
    /// <param name="text">The response text to return.</param>
    private sealed class StaticChatClient(string text) : IChatClient
    {
        /// <inheritdoc />
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));

        /// <inheritdoc />
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();

        /// <inheritdoc />
        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Throws from chat completions to simulate extraction failures.
    /// </summary>
    private sealed class ThrowingChatClient : IChatClient
    {
        /// <inheritdoc />
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromException<ChatResponse>(new InvalidOperationException("boom"));

        /// <inheritdoc />
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();

        /// <inheritdoc />
        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}

#pragma warning restore MAAI001

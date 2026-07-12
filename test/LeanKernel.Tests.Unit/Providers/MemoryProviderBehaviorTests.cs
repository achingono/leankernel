using FluentAssertions;
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

public class MemoryProviderBehaviorTests
{
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

    private static (TestableMemoryProvider Provider, TestAgentSession Session) CreateSut(
        InMemoryMemoryClient memoryClient,
        string? extractionResponse = "[]",
        bool extractionThrows = false)
    {
        var permit = new Mock<IPermit>();
        permit.SetupGet(p => p.TenantId).Returns(Guid.NewGuid());
        permit.SetupGet(p => p.UserId).Returns(Guid.NewGuid());
        permit.SetupGet(p => p.ChannelId).Returns(Guid.NewGuid());
        permit.SetupGet(p => p.SessionId).Returns("permit-session");

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
                new MemoryPageParser(),
                renderer,
                normalizer,
                extraction,
                TimeProvider.System,
                NullLogger<MemoryProvider>.Instance),
            new TestAgentSession());
    }

    private sealed class TestableMemoryProvider(
        IMemoryClient memoryClient,
        IPermit permit,
        MemoryPageParser parser,
        MemoryPageRenderer renderer,
        MemoryPageNormalizer normalizer,
        FactExtractionService factExtractionService,
        TimeProvider timeProvider,
        Microsoft.Extensions.Logging.ILogger<MemoryProvider> logger)
        : MemoryProvider(memoryClient, permit, parser, renderer, normalizer, factExtractionService, timeProvider, logger)
    {
        public async Task<AIContext> ProvideForTestAsync(AIContextProvider.InvokingContext context)
            => await ProvideAIContextAsync(context);

        public async Task StoreForTestAsync(AIContextProvider.InvokedContext context)
            => await StoreAIContextAsync(context);
    }

    private sealed class InMemoryMemoryClient : IMemoryClient
    {
        public IReadOnlyList<MemoryItem> SearchResults { get; init; } = [];
        public List<(string Key, string Content)> Saved { get; } = [];

        public Task<IReadOnlyList<MemoryItem>> SearchMemoriesAsync(MemoryScope scope, string query, int maxResults = 10, CancellationToken ct = default)
        {
            return Task.FromResult(SearchResults);
        }

        public Task SaveMemoryAsync(MemoryScope scope, string key, string content, CancellationToken ct = default)
        {
            Saved.Add((key, content));
            return Task.CompletedTask;
        }
    }

    private sealed class StubReasoningModel : IReasoningModel
    {
        public bool Enabled => false;

        public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, int maxOutputTokens, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class StaticChatClient(string text) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromException<ChatResponse>(new InvalidOperationException("boom"));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type serviceType, object? serviceKey = null)
            => null;

        public void Dispose()
        {
        }
    }
}

#pragma warning restore MAAI001

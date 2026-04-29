using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Thinker;

namespace LeanKernel.Tests.Unit.Thinker;

public class ThinkerServiceTests
{
    private readonly IContextGatekeeper _gatekeeper = Substitute.For<IContextGatekeeper>();
    private readonly ISessionStore _sessions = Substitute.For<ISessionStore>();
    private readonly IWikiStore _wiki = Substitute.For<IWikiStore>();
    private readonly IOptions<LeanKernelConfig> _config = Options.Create(new LeanKernelConfig
    {
        LiteLlm = new LiteLlmConfig
        {
            BaseUrl = "http://localhost:4000",
            ApiKey = "test-key",
            DefaultModel = "gpt-4",
            ContextWindowTokens = 8000
        },
        Wiki = new WikiConfig { BasePath = "/tmp/wiki" }
    });

    private static ConversationContext MakeContext(
        List<ConversationTurn>? history = null,
        List<RelevanceScore>? wikiLeanKernels = null) =>
        new()
        {
            SystemPrompt = "You are LeanKernel.",
            History = history ?? [],
            WikiLeanKernels = wikiLeanKernels ?? [],
            RetrievedLeanKernels = [],
            ActiveToolNames = []
        };

    private ThinkerService CreateService(IChatClient? chatClient = null)
    {
        var client = chatClient ?? new TestChatClient("test response");
        var factory = new AgentFactory(client, NullLogger<AgentFactory>.Instance);
        var registry = Substitute.For<IToolRegistry>();
        registry.Tools.Returns(new Dictionary<string, ITool>());
        var toolAdapter = new ToolFunctionAdapter(registry, NullLogger<ToolFunctionAdapter>.Instance);
        var promptAssembler = new PromptAssembler(NullLogger<PromptAssembler>.Instance);

        return new ThinkerService(
            _gatekeeper, _sessions, _wiki,
            factory, toolAdapter, promptAssembler,
            _config, NullLogger<ThinkerService>.Instance);
    }

    private LeanKernelMessage CreateMessage(string content = "Hello") =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            ChannelId = "test",
            SenderId = "user1",
            Content = content,
            Timestamp = DateTimeOffset.UtcNow
        };

    [Fact]
    public async Task ProcessAsync_ResolvesSession()
    {
        _sessions.GetOrCreateSessionIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("sess1");
        _gatekeeper.GateContextAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<ContextBudget>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MakeContext());

        var svc = CreateService();
        var result = await svc.ProcessAsync(CreateMessage(), CancellationToken.None);

        await _sessions.Received(1).GetOrCreateSessionIdAsync("test", "user1", Arg.Any<CancellationToken>());
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ProcessAsync_RecordsUserTurn()
    {
        _sessions.GetOrCreateSessionIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("sess1");
        _gatekeeper.GateContextAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<ContextBudget>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MakeContext());

        var svc = CreateService();
        await svc.ProcessAsync(CreateMessage("Hi there"), CancellationToken.None);

        await _sessions.Received().AppendTurnAsync("sess1",
            Arg.Is<ConversationTurn>(t => t.Role == "user" && t.Content == "Hi there"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_RecordsAssistantTurn()
    {
        _sessions.GetOrCreateSessionIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("sess1");
        _gatekeeper.GateContextAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<ContextBudget>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MakeContext());

        var svc = CreateService(new TestChatClient("I am LeanKernel"));
        await svc.ProcessAsync(CreateMessage(), CancellationToken.None);

        await _sessions.Received().AppendTurnAsync("sess1",
            Arg.Is<ConversationTurn>(t => t.Role == "assistant" && t.Content == "I am LeanKernel"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_ReturnsLlmResponse()
    {
        _sessions.GetOrCreateSessionIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("sess1");
        _gatekeeper.GateContextAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<ContextBudget>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MakeContext());

        var svc = CreateService(new TestChatClient("Hello World"));
        var result = await svc.ProcessAsync(CreateMessage(), CancellationToken.None);

        Assert.Equal("Hello World", result);
    }

    [Fact]
    public async Task ProcessAsync_WhenLlmFails_ReturnsErrorMessage()
    {
        _sessions.GetOrCreateSessionIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("sess1");
        _gatekeeper.GateContextAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<ContextBudget>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MakeContext());

        var svc = CreateService(new TestChatClient(throwOnCall: true));
        var result = await svc.ProcessAsync(CreateMessage(), CancellationToken.None);

        Assert.Contains("error", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_ContextIsGated()
    {
        _sessions.GetOrCreateSessionIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("sess1");
        _gatekeeper.GateContextAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<ContextBudget>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MakeContext(
                wikiLeanKernels: [new RelevanceScore { EntryId = "e1", Content = "test", Score = 0.9f }],
                history: [new ConversationTurn { Role = "user", Content = "prev", Timestamp = DateTimeOffset.UtcNow }]
            ));

        var svc = CreateService();
        var result = await svc.ProcessAsync(CreateMessage(), CancellationToken.None);

        await _gatekeeper.Received(1).GateContextAsync(
            Arg.Any<LeanKernelMessage>(), Arg.Any<ContextBudget>(), "sess1", Arg.Any<CancellationToken>());
        Assert.NotNull(result);
    }

    [Fact]
    public void BuildMessages_ConvertsHistoryAndAddsCurrentQuery()
    {
        var history = new List<ConversationTurn>
        {
            new() { Role = "user", Content = "Hi", Timestamp = DateTimeOffset.UtcNow },
            new() { Role = "assistant", Content = "Hello!", Timestamp = DateTimeOffset.UtcNow }
        };

        var messages = ThinkerService.BuildMessages(history, "Current question").ToList();

        Assert.Equal(3, messages.Count);
        Assert.Equal(ChatRole.User, messages[0].Role);
        Assert.Equal("Hi", messages[0].Text);
        Assert.Equal(ChatRole.Assistant, messages[1].Role);
        Assert.Equal(ChatRole.User, messages[2].Role);
        Assert.Equal("Current question", messages[2].Text);
    }

    [Fact]
    public void BuildMessages_EmptyHistory_OnlyCurrentQuery()
    {
        var messages = ThinkerService.BuildMessages([], "query").ToList();

        Assert.Single(messages);
        Assert.Equal("query", messages[0].Text);
    }

    private sealed class TestChatClient : IChatClient
    {
        private readonly string _response;
        private readonly bool _throwOnCall;

        public TestChatClient(string response = "test", bool throwOnCall = false)
        {
            _response = response;
            _throwOnCall = throwOnCall;
        }

        public void Dispose() { }
        public ChatClientMetadata Metadata => new();
        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(IChatClient) ? this : null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (_throwOnCall) throw new HttpRequestException("LLM unreachable");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _response)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}

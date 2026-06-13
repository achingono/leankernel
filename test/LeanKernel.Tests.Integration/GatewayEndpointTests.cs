using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Gateway;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LeanKernel.Tests.Integration;

public class GatewayEndpointTests
{
    [Fact]
    public async Task Root_route_serves_the_blazor_shell()
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<title>LeanKernel</title>", html);
        Assert.Contains("LeanKernel", html);
        Assert.Contains("blazor.web.js", html);
    }

    [Fact]
    public async Task Chat_endpoint_returns_response_for_a_valid_request()
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest { Message = "Hello" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("stub-response", payload?["response"]?.GetValue<string>());
        Assert.Equal("generated-api-session", payload?["sessionId"]?.GetValue<string>());
        Assert.Equal("generated-api-session", factory.Runtime.LastMessage?.SessionId);
        Assert.Equal("Hello", factory.Runtime.LastMessage?.Content);
        Assert.Equal("api-user", factory.Runtime.LastMessage?.SenderId);
        Assert.Equal("api", factory.Runtime.LastMessage?.ChannelId);
    }

    [Fact]
    public async Task Chat_endpoint_rejects_requests_with_missing_message()
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest { Message = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Message is required", payload?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task Chat_endpoint_rejects_requests_without_the_required_api_key()
    {
        await using var factory = new GatewayTestApplicationFactory(apiKey: "secret-key");
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest { Message = "Hello" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Chat_endpoint_accepts_requests_with_the_required_api_key()
    {
        await using var factory = new GatewayTestApplicationFactory(apiKey: "secret-key");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest
        {
            Message = "Hello",
            SessionId = "session-123",
            UserId = "user-42",
            ChannelId = "channel-42",
            Metadata = new Dictionary<string, string>
            {
                ["retrieval_scope"] = "personal"
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("stub-response", payload?["response"]?.GetValue<string>());
        Assert.Equal("session-123", payload?["sessionId"]?.GetValue<string>());
        Assert.Equal("session-123", factory.Runtime.LastMessage?.SessionId);
        Assert.Equal("user-42", factory.Runtime.LastMessage?.SenderId);
        Assert.Equal("channel-42", factory.Runtime.LastMessage?.ChannelId);
        Assert.Equal("personal", factory.Runtime.LastMessage?.Metadata?["retrieval_scope"]);
    }

    [Fact]
    public async Task Diagnostics_endpoint_returns_entries_when_authorized()
    {
        await using var factory = new GatewayTestApplicationFactory(apiKey: "secret-key");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");

        var response = await client.GetAsync("/api/diagnostics/session-123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, payload?["count"]?.GetValue<int>());
        Assert.Equal("gateway", payload?["entries"]?[0]?["category"]?.GetValue<string>());
    }

    [Fact]
    public async Task Diagnostics_endpoint_returns_a_message_when_the_sink_is_not_configured()
    {
        await using var factory = new GatewayTestApplicationFactory(apiKey: "secret-key", registerDiagnosticsSink: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");

        var response = await client.GetAsync("/api/diagnostics/session-123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Diagnostics sink not configured", payload?["message"]?.GetValue<string>());
        Assert.Equal(0, payload?["entries"]?.AsArray().Count ?? -1);
    }

    [Fact]
    public async Task Chat_endpoint_uses_forwarded_auth_identity_when_authenticated()
    {
        await using var factory = new GatewayTestApplicationFactory(apiKey: "secret-key", enableForwardedAuth: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");
        client.DefaultRequestHeaders.Add("X-Auth-Request-User", "auth-user-42");
        client.DefaultRequestHeaders.Add("X-Auth-Request-Email", "user@example.com");

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest
        {
            Message = "Hello",
            UserId = "attacker-supplied-id",
            SessionId = "session-123",
            ChannelId = "api"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("stub-response", payload?["response"]?.GetValue<string>());
        Assert.Equal("auth-user-42", factory.Runtime.LastMessage?.SenderId);
        Assert.Equal("session-123", factory.Runtime.LastMessage?.SessionId);
    }

    [Fact]
    public async Task Chat_endpoint_ignores_spoofed_user_id_when_forwarded_auth_is_active()
    {
        await using var factory = new GatewayTestApplicationFactory(apiKey: "secret-key", enableForwardedAuth: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");
        client.DefaultRequestHeaders.Add("X-Auth-Request-User", "real-user");
        client.DefaultRequestHeaders.Add("X-Auth-Request-Email", "real@example.com");

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest
        {
            Message = "Test",
            UserId = "spoofed-user",
            SessionId = "session-123"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("real-user", factory.Runtime.LastMessage?.SenderId);
    }

    [Fact]
    public async Task Chat_endpoint_returns_404_for_unowned_session_when_authenticated()
    {
        await using var factory = new GatewayTestApplicationFactory(apiKey: "secret-key", enableForwardedAuth: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");
        client.DefaultRequestHeaders.Add("X-Auth-Request-User", "user-without-session");
        factory.SessionStore.OwnedSessionIdsByUser.Clear();

        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest
        {
            Message = "Hello",
            SessionId = "session-123"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Chat_endpoint_rejects_unauthenticated_requests_when_forwarded_auth_requires_auth()
    {
        await using var factory = new GatewayTestApplicationFactory(
            apiKey: "secret-key",
            enableForwardedAuth: true,
            forwardedAuthRequireAuthenticatedUser: true);

        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "secret-key");

        factory.SessionStore.OwnedSessionIdsByUser.Remove("attacker-supplied-id");

        // No X-Auth-Request-* headers.
        var response = await client.PostAsJsonAsync("/api/chat", new ChatRequest
        {
            Message = "Hello",
            SessionId = "session-123",
            ChannelId = "api",
            UserId = "attacker-supplied-id"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

public sealed class GatewayTestApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string? _apiKey;
    private readonly bool _registerDiagnosticsSink;
    private readonly bool _enableForwardedAuth;
    private readonly bool _forwardedAuthRequireAuthenticatedUser;

    public GatewayTestApplicationFactory(
        string? apiKey = null,
        bool registerDiagnosticsSink = true,
        bool enableForwardedAuth = false,
        bool forwardedAuthRequireAuthenticatedUser = false)
    {
        _apiKey = apiKey;
        _registerDiagnosticsSink = registerDiagnosticsSink;
        _enableForwardedAuth = enableForwardedAuth;
        _forwardedAuthRequireAuthenticatedUser = forwardedAuthRequireAuthenticatedUser;
    }

    public RecordingAgentRuntime Runtime { get; } = new();
    public RecordingSessionStore SessionStore { get; } = new();
    public StubContextDiagnosticsService ContextDiagnosticsService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var configValues = new Dictionary<string, string?>
            {
                ["LeanKernel:Gateway:ApiKey"] = _apiKey ?? string.Empty,
                ["LeanKernel:Database:ConnectionString"] = "Host=localhost;Database=leankernel;Username=leankernel;Password=leankernel",
                ["LeanKernel:GBrain:BaseUrl"] = "http://localhost:8789",
                ["LeanKernel:GBrain:TimeoutSeconds"] = "1",
                ["LeanKernel:LiteLlm:BaseUrl"] = "http://localhost:4000",
                ["LeanKernel:LiteLlm:ApiKey"] = "test-key",
                ["LeanKernel:LiteLlm:DefaultModel"] = "gpt-4o-mini",
                ["LeanKernel:LiteLlm:ContextWindowTokens"] = "128",
                ["LeanKernel:Diagnostics:Enabled"] = "true",
                ["LeanKernel:Diagnostics:PersistToDatabase"] = "true",
                ["LeanKernel:Diagnostics:ContextDiagnosticsEnabled"] = "true",
                ["LeanKernel:Diagnostics:MaxDiagnosticsPerSession"] = "100",
                ["LeanKernel:ForwardedAuth:Enabled"] = _enableForwardedAuth ? "true" : "false",
                ["LeanKernel:ForwardedAuth:RequireAuthenticatedUser"] = (_enableForwardedAuth && _forwardedAuthRequireAuthenticatedUser) ? "true" : "false",
            };

            configuration.AddInMemoryCollection(configValues);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAgentRuntime>();
            services.RemoveAll<ISessionStore>();
            services.RemoveAll<IKnowledgeService>();
            services.RemoveAll<IDiagnosticsSink>();
            services.RemoveAll<IContextDiagnosticsService>();

            services.AddSingleton<IAgentRuntime>(Runtime);
            services.AddSingleton<ISessionStore>(SessionStore);
            services.AddSingleton<IKnowledgeService>(new StubKnowledgeService());
            services.AddSingleton<IContextDiagnosticsService>(ContextDiagnosticsService);

            if (_registerDiagnosticsSink)
            {
                services.AddSingleton<IDiagnosticsSink>(new StubDiagnosticsSink());
            }
        });
    }

    public sealed class RecordingAgentRuntime : IAgentRuntime
    {
        public LeanKernelMessage? LastMessage { get; private set; }

        public Task<string> RunTurnAsync(LeanKernelMessage message, CancellationToken ct = default)
        {
            LastMessage = message;
            return Task.FromResult("stub-response");
        }
    }

    public sealed class RecordingSessionStore : ISessionStore
    {
        public string GeneratedSessionId { get; set; } = "generated-api-session";
        public Dictionary<string, HashSet<string>> OwnedSessionIdsByUser { get; set; } = new(StringComparer.Ordinal)
        {
            ["user-42"] = new HashSet<string>(StringComparer.Ordinal) { "session-123" },
            ["auth-user-42"] = new HashSet<string>(StringComparer.Ordinal) { "session-123" },
            ["real-user"] = new HashSet<string>(StringComparer.Ordinal) { "session-123" },
        };

        public Task<string> GetOrCreateSessionIdAsync(string channelId, string userId, CancellationToken ct = default)
            => Task.FromResult(GeneratedSessionId);

        public Task AppendTurnAsync(string sessionId, ConversationTurn turn, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ConversationTurn>> GetHistoryAsync(string sessionId, int maxTurns = 50, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ConversationTurn>>([]);

        public Task<bool> SessionBelongsToUserAsync(string sessionId, string userId, CancellationToken ct = default)
            => Task.FromResult(OwnedSessionIdsByUser.TryGetValue(userId, out var owned)
                && owned.Contains(sessionId));
    }

    public sealed class StubContextDiagnosticsService : IContextDiagnosticsService
    {
        private readonly List<StoredTurn> _turns = [];

        public StubContextDiagnosticsService()
        {
            Reset();
        }

        public void Clear() => _turns.Clear();

        public Task StoreContextDiagnosticsAsync(string sessionId, string turnId, ContextDiagnosticsSnapshot snapshot, CancellationToken ct = default)
        {
            _turns.RemoveAll(turn => turn.SessionId == sessionId && turn.TurnId == turnId);
            _turns.Add(CreateStoredTurn(sessionId, turnId, snapshot));
            return Task.CompletedTask;
        }

        public Task<ContextDiagnosticsResponse?> GetContextDiagnosticsAsync(string sessionId, string? turnId = null, CancellationToken ct = default)
            => Task.FromResult(Resolve(sessionId, turnId)?.Context);

        public Task<BudgetDiagnosticsResponse?> GetBudgetDiagnosticsAsync(string sessionId, string? turnId = null, CancellationToken ct = default)
            => Task.FromResult(Resolve(sessionId, turnId)?.Budget);

        public Task<HistoryDiagnosticsResponse?> GetHistoryDiagnosticsAsync(string sessionId, string? turnId = null, CancellationToken ct = default)
            => Task.FromResult(Resolve(sessionId, turnId)?.History);

        private void Reset()
        {
            _turns.Clear();
            _turns.Add(CreateStoredTurn("session-123", "turn-1", new ContextDiagnosticsSnapshot
            {
                Admissions =
                [
                    new ContextAdmissionRecord { Key = "wiki-1", Source = "wiki", Score = 0.92, TokenCount = 4, Admitted = true },
                ],
                BudgetUsage = new ContextBudgetUsage
                {
                    SystemPromptUsed = 10,
                    WikiFactsUsed = 8,
                    RetrievalUsed = 6,
                    ConversationUsed = 12,
                    ToolsUsed = 2,
                },
                Budget = new ContextBudget
                {
                    TotalTokens = 96,
                    SystemPromptBudget = 15,
                    WikiFactsBudget = 20,
                    RetrievalBudget = 20,
                    ConversationBudget = 36,
                    ToolsBudget = 5,
                },
                TotalBudgetTokens = 128,
                ResponseHeadroomRatio = 0.25,
                HistoryDiagnostics = new HistoryShapingDiagnostics
                {
                    TotalTurns = 5,
                    VerbatimTurns = 2,
                    CompactedTurns = 1,
                    SummarizedTurns = 0,
                    DroppedTurns = 2,
                    TotalTokensBefore = 80,
                    TotalTokensAfter = 52,
                    BudgetAvailable = 60,
                },
                RetrievalDiagnostics = new RetrievalDiagnostics
                {
                    SessionId = "session-123",
                    TurnId = "turn-1",
                    TotalConsidered = 1,
                    TotalAdmitted = 1,
                    TotalExcludedByScope = 0,
                    TotalExcludedByScore = 0,
                    EffectiveScope = "personal",
                },
                Timestamp = DateTimeOffset.Parse("2025-05-20T10:00:00Z"),
            }));
            _turns.Add(CreateStoredTurn("session-123", "turn-2", new ContextDiagnosticsSnapshot
            {
                Admissions =
                [
                    new ContextAdmissionRecord { Key = "wiki-2", Source = "wiki", Score = 0.95, TokenCount = 5, Admitted = true },
                    new ContextAdmissionRecord { Key = "doc-2", Source = "gbrain", Score = 0.2, TokenCount = 6, Admitted = false, ExclusionReason = "BudgetExhausted" },
                ],
                BudgetUsage = new ContextBudgetUsage
                {
                    SystemPromptUsed = 11,
                    WikiFactsUsed = 10,
                    RetrievalUsed = 7,
                    ConversationUsed = 15,
                    ToolsUsed = 2,
                },
                Budget = new ContextBudget
                {
                    TotalTokens = 96,
                    SystemPromptBudget = 15,
                    WikiFactsBudget = 20,
                    RetrievalBudget = 20,
                    ConversationBudget = 36,
                    ToolsBudget = 5,
                },
                TotalBudgetTokens = 128,
                ResponseHeadroomRatio = 0.25,
                HistoryDiagnostics = new HistoryShapingDiagnostics
                {
                    TotalTurns = 6,
                    VerbatimTurns = 2,
                    CompactedTurns = 2,
                    SummarizedTurns = 0,
                    DroppedTurns = 2,
                    TotalTokensBefore = 90,
                    TotalTokensAfter = 58,
                    BudgetAvailable = 60,
                },
                RetrievalDiagnostics = new RetrievalDiagnostics
                {
                    SessionId = "session-123",
                    TurnId = "turn-2",
                    TotalConsidered = 2,
                    TotalAdmitted = 1,
                    TotalExcludedByScope = 0,
                    TotalExcludedByScore = 1,
                    EffectiveScope = "personal",
                },
                Timestamp = DateTimeOffset.Parse("2025-05-20T10:05:00Z"),
            }));
        }

        private StoredTurn? Resolve(string sessionId, string? turnId)
        {
            var sessionTurns = _turns
                .Where(turn => turn.SessionId == sessionId)
                .OrderBy(turn => turn.Timestamp)
                .ToList();

            if (!string.IsNullOrWhiteSpace(turnId))
            {
                return sessionTurns.LastOrDefault(turn => turn.TurnId == turnId);
            }

            return sessionTurns.LastOrDefault();
        }

        private static StoredTurn CreateStoredTurn(string sessionId, string turnId, ContextDiagnosticsSnapshot snapshot)
        {
            var admissions = snapshot.Admissions;
            var admitted = admissions.Count(admission => admission.Admitted);
            var history = snapshot.HistoryDiagnostics;

            return new StoredTurn(
                sessionId,
                turnId,
                snapshot.Timestamp,
                new ContextDiagnosticsResponse
                {
                    SessionId = sessionId,
                    TurnId = turnId,
                    Timestamp = snapshot.Timestamp,
                    Admissions = admissions,
                    TotalCandidatesConsidered = admissions.Count,
                    TotalAdmitted = admitted,
                    TotalExcluded = admissions.Count - admitted,
                    RetrievalDiagnostics = snapshot.RetrievalDiagnostics,
                },
                new BudgetDiagnosticsResponse
                {
                    SessionId = sessionId,
                    TurnId = turnId,
                    TotalBudgetTokens = snapshot.TotalBudgetTokens,
                    UsableBudgetTokens = snapshot.Budget.TotalTokens,
                    ResponseHeadroomRatio = snapshot.ResponseHeadroomRatio,
                    Usage = snapshot.BudgetUsage,
                    SystemPrompt = CreateBudgetDetail(snapshot.Budget.SystemPromptBudget, snapshot.BudgetUsage.SystemPromptUsed),
                    WikiFacts = CreateBudgetDetail(snapshot.Budget.WikiFactsBudget, snapshot.BudgetUsage.WikiFactsUsed),
                    Retrieval = CreateBudgetDetail(snapshot.Budget.RetrievalBudget, snapshot.BudgetUsage.RetrievalUsed),
                    Conversation = CreateBudgetDetail(snapshot.Budget.ConversationBudget, snapshot.BudgetUsage.ConversationUsed),
                    Tools = CreateBudgetDetail(snapshot.Budget.ToolsBudget, snapshot.BudgetUsage.ToolsUsed),
                },
                new HistoryDiagnosticsResponse
                {
                    SessionId = sessionId,
                    TurnId = turnId,
                    Shaping = history,
                    VerbatimTurns = history?.VerbatimTurns ?? 0,
                    CompactedTurns = history?.CompactedTurns ?? 0,
                    SummarizedTurns = history?.SummarizedTurns ?? 0,
                    DroppedTurns = history?.DroppedTurns ?? 0,
                    TokensSaved = history is null ? 0 : Math.Max(0, history.TotalTokensBefore - history.TotalTokensAfter),
                });
        }

        private static BudgetCategoryDetail CreateBudgetDetail(int allocated, int used)
            => new()
            {
                Allocated = allocated,
                Used = used,
            };

        private sealed record StoredTurn(
            string SessionId,
            string TurnId,
            DateTimeOffset Timestamp,
            ContextDiagnosticsResponse Context,
            BudgetDiagnosticsResponse Budget,
            HistoryDiagnosticsResponse History);
    }

    private sealed class StubKnowledgeService : IKnowledgeService
    {
        public Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default)
            => Task.FromResult<KnowledgePage?>(null);

        public Task PutPageAsync(string key, string content, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeletePageAsync(string key, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RetrievalCandidate>>([]);
    }

    private sealed class StubDiagnosticsSink : IDiagnosticsSink
    {
        public Task<IReadOnlyList<DiagnosticEntry>> GetEntriesAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DiagnosticEntry>>(
            [
                new DiagnosticEntry
                {
                    SessionId = sessionId,
                    Category = "gateway",
                    Payload = new { message = "ok" }
                }
            ]);

        public Task RecordAsync(DiagnosticEntry entry, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}

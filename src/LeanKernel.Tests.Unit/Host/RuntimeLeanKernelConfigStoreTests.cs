using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Services;

namespace LeanKernel.Tests.Unit.Host;

public class RuntimeLeanKernelConfigStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LeanKernelHostPaths _paths;

    public RuntimeLeanKernelConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_runtimecfg_{Guid.NewGuid():N}");
        _paths = new LeanKernelHostPaths
        {
            DataDirectory = _tempDir,
            AgentsDirectory = Path.Combine(_tempDir, "agents"),
            RuntimeConfigPath = Path.Combine(_tempDir, "runtime-settings.json"),
            OnboardingStatePath = Path.Combine(_tempDir, "onboarding-state.json")
        };
    }

    [Fact]
    public async Task SaveAsync_WritesRuntimeSettingsFile()
    {
        var current = new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig { BaseUrl = "http://litellm:4000", ApiKey = "key" },
            Qdrant = new QdrantConfig { Host = "qdrant", Port = 6334 },
            Signal = new SignalConfig
            {
                Enabled = false,
                CliPath = "/usr/local/bin/signal-cli",
                Account = "",
                AllowedSenders = ["+15551234567"]
            },
            Wiki = new WikiConfig { BasePath = "/app/data/wiki" },
            Context = new ContextConfig(),
            Scheduler = new SchedulerConfig()
        };

        var store = new RuntimeLeanKernelConfigStore(
            new TestOptionsMonitor(current),
            _paths,
            NullLogger<RuntimeLeanKernelConfigStore>.Instance);

        await store.SaveAsync(current, CancellationToken.None);

        Assert.True(File.Exists(_paths.RuntimeConfigPath));
        var json = await File.ReadAllTextAsync(_paths.RuntimeConfigPath);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("leanKernel", out var leanKernel));
        var sender = leanKernel.GetProperty("signal")
            .GetProperty("allowedSenders")[0]
            .GetString();
        Assert.Equal("+15551234567", sender);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsAllConfigSections()
    {
        var current = new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig { BaseUrl = "http://litellm:4000", ApiKey = "key" },
            Qdrant = new QdrantConfig { Host = "qdrant", Port = 6334 },
            Signal = new SignalConfig
            {
                Enabled = true,
                CliPath = "/usr/bin/signal-cli",
                Account = "+15559990000",
                AllowedSenders = ["+15551234567"],
                DaemonBaseUrl = "http://signal-daemon:8080"
            },
            Unstructured = new UnstructuredConfig
            {
                Enabled = true,
                BaseUrl = "http://unstructured:8000",
                TimeoutSeconds = 90
            },
            Agents = new AgentsConfig { BasePath = "/app/data/agents" },
            Wiki = new WikiConfig { BasePath = "/app/data/wiki" },
            Context = new ContextConfig(),
            Scheduler = new SchedulerConfig(),
            Auth = new AuthConfig
            {
                Mode = AuthMode.LocalPasscode,
                AllowedOrigins = ["https://example.com"],
                Local = new LocalPasscodeConfig { MinLength = 10 },
                Oidc = new OidcConfig { Authority = "https://auth.example.com" },
                RateLimit = new RateLimitConfig { LoginPerMinutePerIp = 3 }
            },
            Knowledge = new KnowledgeConfig
            {
                Enabled = true,
                CollectionName = "LEANKERNEL_knowledge",
                EmbeddingDimension = 1536,
                DocumentsPath = "/app/data/docs",
                DefaultDocumentTags = ["general"],
                AgentScopes = new Dictionary<string, AgentScopeConfig>
                {
                    ["main"] = new AgentScopeConfig { Tags = ["all"], Description = "Main agent" }
                },
                TagRules = [new TagRule { PathPattern = "*.md", Tags = ["markdown"] }]
            },
            Routing = new RoutingConfig
            {
                Enabled = true,
                ShadowMode = false,
                EnableQualityEscalation = true,
                SmallMaxTokens = 2000,
                SmallAlias = "small",
                SpendGuard = new SpendGuardConfig
                {
                    DailyPaidRequestSoftLimit = 100,
                    DailyPaidRequestHardLimit = 200
                }
            },
            SignalPhoneNumber = "+15550001111",
            SignalServerUrl = "https://signal-server.example.com",
            SignalApiToken = "tok-abc",
            DiscordBotToken = "discord-tok",
            DiscordChannelId = "123456789"
        };

        var store = new RuntimeLeanKernelConfigStore(
            new TestOptionsMonitor(current),
            _paths,
            NullLogger<RuntimeLeanKernelConfigStore>.Instance);

        await store.SaveAsync(current, CancellationToken.None);

        var json = await File.ReadAllTextAsync(_paths.RuntimeConfigPath);
        using var doc = JsonDocument.Parse(json);
        var leanKernel = doc.RootElement.GetProperty("leanKernel");

        Assert.Equal("http://signal-daemon:8080",
            leanKernel.GetProperty("signal").GetProperty("daemonBaseUrl").GetString());
        Assert.Equal("+15559990000",
            leanKernel.GetProperty("signal").GetProperty("account").GetString());
        Assert.Equal("http://unstructured:8000",
            leanKernel.GetProperty("unstructured").GetProperty("baseUrl").GetString());
        Assert.Equal(90,
            leanKernel.GetProperty("unstructured").GetProperty("timeoutSeconds").GetInt32());
        Assert.Equal("/app/data/agents",
            leanKernel.GetProperty("agents").GetProperty("basePath").GetString());
        Assert.True(leanKernel.GetProperty("routing").GetProperty("enabled").GetBoolean());
        Assert.False(leanKernel.GetProperty("routing").GetProperty("shadowMode").GetBoolean());
        Assert.Equal(100,
            leanKernel.GetProperty("routing").GetProperty("spendGuard")
                .GetProperty("dailyPaidRequestSoftLimit").GetInt32());
        Assert.Equal("+15550001111",
            leanKernel.GetProperty("signalPhoneNumber").GetString());
        Assert.Equal("discord-tok",
            leanKernel.GetProperty("discordBotToken").GetString());
    }

    [Fact]
    public async Task GetCurrent_ClonesNewFieldsCorrectly()
    {
        var source = new LeanKernelConfig
        {
            Signal = new SignalConfig
            {
                DaemonBaseUrl = "http://daemon:8080",
                AllowedSenders = ["+1111", "+2222"]
            },
            Unstructured = new UnstructuredConfig { BaseUrl = "http://unstructured:8000", TimeoutSeconds = 60 },
            Agents = new AgentsConfig { BasePath = "/agents" },
            Routing = new RoutingConfig
            {
                Enabled = true,
                SmallAlias = "s",
                SpendGuard = new SpendGuardConfig { DailyPaidRequestSoftLimit = 50 }
            },
            SignalPhoneNumber = "+13339990000",
            DiscordBotToken = "bot-tok"
        };

        var store = new RuntimeLeanKernelConfigStore(
            new TestOptionsMonitor(source),
            _paths,
            NullLogger<RuntimeLeanKernelConfigStore>.Instance);

        var clone = store.GetCurrent();

        Assert.Equal("http://daemon:8080", clone.Signal.DaemonBaseUrl);
        Assert.Equal(["+1111", "+2222"], clone.Signal.AllowedSenders);
        Assert.Equal("http://unstructured:8000", clone.Unstructured.BaseUrl);
        Assert.Equal(60, clone.Unstructured.TimeoutSeconds);
        Assert.Equal("/agents", clone.Agents.BasePath);
        Assert.True(clone.Routing.Enabled);
        Assert.Equal(50, clone.Routing.SpendGuard.DailyPaidRequestSoftLimit);
        Assert.Equal("+13339990000", clone.SignalPhoneNumber);
        Assert.Equal("bot-tok", clone.DiscordBotToken);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<LeanKernelConfig>
    {
        private readonly LeanKernelConfig _value;

        public TestOptionsMonitor(LeanKernelConfig value)
        {
            _value = value;
        }

        public LeanKernelConfig CurrentValue => _value;
        public LeanKernelConfig Get(string? name) => _value;
        public IDisposable? OnChange(Action<LeanKernelConfig, string?> listener) => null;
    }
}

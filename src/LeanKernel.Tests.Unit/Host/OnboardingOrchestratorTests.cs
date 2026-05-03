using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Services;

namespace LeanKernel.Tests.Unit.Host;

public class OnboardingOrchestratorTests : IDisposable
{
    private readonly string _tempDir;

    public OnboardingOrchestratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_onboarding_orch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task ValidateAsync_AllChecksPassing_ReturnsSuccess()
    {
        var cfg = MakeConfig(new SignalConfig
        {
            Enabled = true,
            Account = "+1234567890",
            CliPath = "/bin/sh"
        });
        var runtime = new StubRuntimeConfigStore(cfg);
        var state = new StubOnboardingStateStore();
        var orchestrator = CreateOrchestrator(
            runtime,
            state,
            CreateFactory(HttpStatusCode.OK, HttpStatusCode.OK),
            (_, _) => Task.FromResult(new OnboardingStepResult
            {
                Step = "qdrant",
                Success = true,
                Message = "ok"
            }));

        var result = await orchestrator.ValidateAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(5, result.Steps.Count);
        Assert.All(result.Steps, s => Assert.True(s.Success));
    }

    [Fact]
    public async Task ValidateAsync_SignalEnabledWithoutAccount_FailsSignalStep()
    {
        var cfg = MakeConfig(new SignalConfig
        {
            Enabled = true,
            Account = "",
            CliPath = "/bin/sh"
        });
        var orchestrator = CreateOrchestrator(
            new StubRuntimeConfigStore(cfg),
            new StubOnboardingStateStore(),
            CreateFactory(HttpStatusCode.OK, HttpStatusCode.OK),
            (_, _) => Task.FromResult(new OnboardingStepResult
            {
                Step = "qdrant",
                Success = true,
                Message = "ok"
            }));

        var result = await orchestrator.ValidateAsync(CancellationToken.None);
        var signal = Assert.Single(result.Steps.Where(s => s.Step == "signal"));

        Assert.False(signal.Success);
        Assert.Contains("no account", signal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ValidateAsync_MissingLiteLlmBaseUrl_FailsLiteLlmStep()
    {
        var cfg = MakeConfig(new SignalConfig { Enabled = false });
        cfg.LiteLlm.BaseUrl = "";
        var orchestrator = CreateOrchestrator(
            new StubRuntimeConfigStore(cfg),
            new StubOnboardingStateStore(),
            CreateFactory(HttpStatusCode.OK, HttpStatusCode.OK),
            (_, _) => Task.FromResult(new OnboardingStepResult
            {
                Step = "qdrant",
                Success = true,
                Message = "ok"
            }));

        var result = await orchestrator.ValidateAsync(CancellationToken.None);
        var lite = Assert.Single(result.Steps.Where(s => s.Step == "litellm-models"));

        Assert.False(lite.Success);
        Assert.Contains("required", lite.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ValidateAsync_ConfiguredEmbeddingMissing_SkipsEmbeddingProbe()
    {
        var cfg = MakeConfig(new SignalConfig { Enabled = false });
        var orchestrator = CreateOrchestrator(
            new StubRuntimeConfigStore(cfg),
            new StubOnboardingStateStore(),
            CreateFactory(HttpStatusCode.OK, HttpStatusCode.BadRequest),
            (_, _) => Task.FromResult(new OnboardingStepResult
            {
                Step = "qdrant",
                Success = true,
                Message = "ok"
            }));

        var result = await orchestrator.ValidateAsync(CancellationToken.None);
        var embeddings = Assert.Single(result.Steps.Where(s => s.Step == "embeddings"));

        Assert.True(embeddings.Success);
        Assert.Contains("skipping embedding probe", embeddings.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveDraftAsync_MergesValuesAndMarksInProgress()
    {
        var current = MakeConfig(new SignalConfig
        {
            Enabled = false,
            Account = "",
            CliPath = "/usr/local/bin/signal-cli"
        });
        var runtime = new StubRuntimeConfigStore(current);
        var state = new StubOnboardingStateStore();
        var orchestrator = CreateOrchestrator(
            runtime,
            state,
            CreateFactory(HttpStatusCode.OK, HttpStatusCode.OK),
            (_, _) => Task.FromResult(new OnboardingStepResult
            {
                Step = "qdrant",
                Success = true,
                Message = "ok"
            }));

        var draft = new OnboardingConfigInput
        {
            LiteLlm = new LiteLlmConfig
            {
                BaseUrl = "http://new-litellm:4000/",
                ApiKey = "",
                DefaultModel = "custom-model",
                EmbeddingModel = "embedding-small",
                ContextWindowTokens = 0
            },
            Qdrant = new QdrantConfig
            {
                Host = "new-qdrant",
                Port = 7000,
                CollectionName = "custom_collection",
                EmbeddingDimension = 2048
            },
            Signal = new SignalConfig
            {
                Enabled = true,
                Account = " +19999999 ",
                CliPath = ""
            },
            Wiki = new WikiConfig
            {
                BasePath = Path.Combine(_tempDir, "wiki2"),
                MaxFactsPerEntry = 30,
                StaleFactDays = 45,
                MinConfidenceThreshold = 0.7
            },
            Scheduler = new SchedulerConfig
            {
                Enabled = true,
                WikiMaintenanceCron = "*/5 * * * *"
            }
        };

        var status = await orchestrator.SaveDraftAsync(draft, CancellationToken.None);
        var saved = runtime.GetCurrent();

        Assert.False(status.Completed);
        Assert.Equal(1, runtime.SaveCount);
        Assert.Equal(1, state.MarkInProgressCount);
        Assert.Equal("http://new-litellm:4000", saved.LiteLlm.BaseUrl); // normalized
        Assert.Equal(current.LiteLlm.ApiKey, saved.LiteLlm.ApiKey); // fallback because blank
        Assert.Equal("custom-model", saved.LiteLlm.DefaultModel);
        Assert.Equal(current.LiteLlm.ContextWindowTokens, saved.LiteLlm.ContextWindowTokens); // fallback because 0
        Assert.Equal("new-qdrant", saved.Qdrant.Host);
        Assert.Equal(7000, saved.Qdrant.Port);
        Assert.Equal("custom_collection", saved.Qdrant.CollectionName);
        Assert.Equal(2048, saved.Qdrant.EmbeddingDimension);
        Assert.Equal("+19999999", saved.Signal.Account);
        Assert.Equal(current.Signal.CliPath, saved.Signal.CliPath); // fallback because blank
        Assert.True(saved.Signal.Enabled);
    }

    [Fact]
    public async Task CompleteAsync_WhenValidationFails_DoesNotMarkCompleted()
    {
        var cfg = MakeConfig(new SignalConfig { Enabled = false });
        var runtime = new StubRuntimeConfigStore(cfg);
        var state = new StubOnboardingStateStore();
        var orchestrator = CreateOrchestrator(
            runtime,
            state,
            CreateFactory(HttpStatusCode.InternalServerError, HttpStatusCode.OK),
            (_, _) => Task.FromResult(new OnboardingStepResult
            {
                Step = "qdrant",
                Success = false,
                Message = "failed"
            }));

        var result = await orchestrator.CompleteAsync(CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, state.MarkCompletedCount);
    }

    [Fact]
    public async Task CompleteAsync_WhenValidationPasses_MarksCompleted()
    {
        var cfg = MakeConfig(new SignalConfig
        {
            Enabled = true,
            Account = "+1444",
            CliPath = "/bin/sh"
        });
        var runtime = new StubRuntimeConfigStore(cfg);
        var state = new StubOnboardingStateStore();
        var orchestrator = CreateOrchestrator(
            runtime,
            state,
            CreateFactory(HttpStatusCode.OK, HttpStatusCode.OK),
            (_, _) => Task.FromResult(new OnboardingStepResult
            {
                Step = "qdrant",
                Success = true,
                Message = "ok"
            }));

        var result = await orchestrator.CompleteAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, state.MarkCompletedCount);
        Assert.True(state.State.Completed);
    }

    [Fact]
    public async Task ValidateAsync_RealQdrantProbe_UnreachableHost_FailsQdrantStep()
    {
        var cfg = MakeConfig(new SignalConfig { Enabled = false });
        cfg.Qdrant.Host = "127.0.0.1";
        cfg.Qdrant.Port = 1;

        var orchestrator = new OnboardingOrchestrator(
            new StubOnboardingStateStore(),
            new StubRuntimeConfigStore(cfg),
            CreateFactory(HttpStatusCode.OK, HttpStatusCode.OK),
            NullLogger<OnboardingOrchestrator>.Instance);

        var result = await orchestrator.ValidateAsync(CancellationToken.None);
        var qdrant = Assert.Single(result.Steps.Where(s => s.Step == "qdrant"));

        Assert.False(qdrant.Success);
    }

    private OnboardingOrchestrator CreateOrchestrator(
        StubRuntimeConfigStore runtime,
        StubOnboardingStateStore state,
        IHttpClientFactory httpClientFactory,
        Func<LeanKernelConfig, CancellationToken, Task<OnboardingStepResult>> qdrantValidator)
    {
        return new OnboardingOrchestrator(
            state,
            runtime,
            httpClientFactory,
            NullLogger<OnboardingOrchestrator>.Instance,
            qdrantValidator);
    }

    private IHttpClientFactory CreateFactory(HttpStatusCode modelStatus, HttpStatusCode embeddingStatus)
    {
        var handler = new RoutingHttpHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("/v1/models", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(modelStatus)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { data = new[] { new { id = "model-a" } } }),
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (path.EndsWith("/v1/embeddings", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(embeddingStatus)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            data = new[] { new { index = 0, embedding = new[] { 0.1f, 0.2f } } }
                        }),
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = new HttpClient(handler);
        return new StubHttpClientFactory(client);
    }

    private LeanKernelConfig MakeConfig(SignalConfig signal) => new()
    {
        LiteLlm = new LiteLlmConfig
        {
            BaseUrl = "http://litellm:4000",
            ApiKey = "sk-test",
            DefaultModel = "small",
            EmbeddingModel = "embedding-small",
            ContextWindowTokens = 128000
        },
        Qdrant = new QdrantConfig
        {
            Host = "qdrant",
            Port = 6334,
            CollectionName = "LEANKERNEL_wiki",
            EmbeddingDimension = 1536
        },
        Signal = signal,
        Wiki = new WikiConfig
        {
            BasePath = Path.Combine(_tempDir, "wiki"),
            MaxFactsPerEntry = 20,
            StaleFactDays = 30,
            MinConfidenceThreshold = 0.5
        },
        Context = new ContextConfig(),
        Scheduler = new SchedulerConfig
        {
            Enabled = true,
            WikiMaintenanceCron = "0 3 * * *"
        }
    };

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class RoutingHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _resolver;

        public RoutingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> resolver)
        {
            _resolver = resolver;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_resolver(request));
        }
    }

    private sealed class StubOnboardingStateStore : IOnboardingStateStore
    {
        public OnboardingStateDocument State { get; private set; } = new()
        {
            Completed = false,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        public int MarkInProgressCount { get; private set; }
        public int MarkCompletedCount { get; private set; }

        public Task<OnboardingStateDocument> GetAsync(CancellationToken ct)
            => Task.FromResult(State);

        public Task<bool> IsCompletedAsync(CancellationToken ct)
            => Task.FromResult(State.Completed);

        public Task MarkInProgressAsync(CancellationToken ct)
        {
            MarkInProgressCount++;
            if (!State.Completed)
            {
                State = new OnboardingStateDocument
                {
                    Completed = false,
                    CompletedAt = null,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Version = State.Version
                };
            }

            return Task.CompletedTask;
        }

        public Task MarkCompletedAsync(CancellationToken ct)
        {
            MarkCompletedCount++;
            State = new OnboardingStateDocument
            {
                Completed = true,
                CompletedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                Version = State.Version
            };
            return Task.CompletedTask;
        }
    }

    private sealed class StubRuntimeConfigStore : IRuntimeLeanKernelConfigStore
    {
        private LeanKernelConfig _current;
        public int SaveCount { get; private set; }

        public StubRuntimeConfigStore(LeanKernelConfig initial)
        {
            _current = initial;
        }

        public LeanKernelConfig GetCurrent() => _current;

        public Task SaveAsync(LeanKernelConfig config, CancellationToken ct)
        {
            SaveCount++;
            _current = config;
            return Task.CompletedTask;
        }
    }
}

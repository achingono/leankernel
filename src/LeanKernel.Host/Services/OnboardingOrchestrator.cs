using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;

namespace LeanKernel.Host.Services;

public sealed class OnboardingOrchestrator : IOnboardingOrchestrator
{
    private readonly IOnboardingStateStore _stateStore;
    private readonly IRuntimeLeanKernelConfigStore _runtimeConfigStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<LeanKernelConfig, CancellationToken, Task<OnboardingStepResult>> _qdrantValidator;
    private readonly ILogger<OnboardingOrchestrator> _logger;

    public OnboardingOrchestrator(
        IOnboardingStateStore stateStore,
        IRuntimeLeanKernelConfigStore runtimeConfigStore,
        IHttpClientFactory httpClientFactory,
        ILogger<OnboardingOrchestrator> logger,
        Func<LeanKernelConfig, CancellationToken, Task<OnboardingStepResult>>? qdrantValidator = null)
    {
        _stateStore = stateStore;
        _runtimeConfigStore = runtimeConfigStore;
        _httpClientFactory = httpClientFactory;
        _qdrantValidator = qdrantValidator ?? ValidateQdrantAsync;
        _logger = logger;
    }

    public async Task<OnboardingStatus> GetStatusAsync(CancellationToken ct)
    {
        var state = await _stateStore.GetAsync(ct);
        return new OnboardingStatus
        {
            Completed = state.Completed,
            CompletedAt = state.CompletedAt,
            UpdatedAt = state.UpdatedAt
        };
    }

    public Task<OnboardingConfigInput> GetDraftAsync(CancellationToken ct)
    {
        var current = _runtimeConfigStore.GetCurrent();
        var draft = new OnboardingConfigInput
        {
            LiteLlm = current.LiteLlm,
            Qdrant = current.Qdrant,
            Signal = current.Signal,
            Wiki = current.Wiki,
            Scheduler = current.Scheduler
        };
        return Task.FromResult(draft);
    }

    public async Task<OnboardingStatus> SaveDraftAsync(OnboardingConfigInput draft, CancellationToken ct)
    {
        var normalized = BuildMergedConfig(_runtimeConfigStore.GetCurrent(), draft);
        await _runtimeConfigStore.SaveAsync(normalized, ct);
        await _stateStore.MarkInProgressAsync(ct);
        return await GetStatusAsync(ct);
    }

    public async Task<OnboardingValidationResult> ValidateAsync(CancellationToken ct)
    {
        var cfg = _runtimeConfigStore.GetCurrent();
        var result = new OnboardingValidationResult
        {
            Steps =
            [
                await ValidateFilesystemAsync(cfg, ct),
                await ValidateLiteLlmModelsAsync(cfg, ct),
                await ValidateEmbeddingEndpointAsync(cfg, ct),
                await _qdrantValidator(cfg, ct),
                ValidateSignal(cfg)
            ]
        };

        return result;
    }

    public async Task<OnboardingCompletionResult> CompleteAsync(CancellationToken ct)
    {
        var validation = await ValidateAsync(ct);
        if (!validation.Success)
        {
            return new OnboardingCompletionResult
            {
                Success = false,
                Message = "Onboarding cannot be completed until all validation checks pass.",
                Status = await GetStatusAsync(ct),
                Validation = validation
            };
        }

        await _stateStore.MarkCompletedAsync(ct);
        _logger.LogInformation("Onboarding marked as completed");

        return new OnboardingCompletionResult
        {
            Success = true,
            Message = "Onboarding completed. LeanKernel is ready.",
            Status = await GetStatusAsync(ct),
            Validation = validation
        };
    }

    private static LeanKernelConfig BuildMergedConfig(LeanKernelConfig current, OnboardingConfigInput draft)
    {
        var merged = new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig
            {
                BaseUrl = NormalizeUrl(draft.LiteLlm.BaseUrl, current.LiteLlm.BaseUrl),
                ApiKey = NormalizeRequired(draft.LiteLlm.ApiKey, current.LiteLlm.ApiKey),
                DefaultModel = NormalizeRequired(draft.LiteLlm.DefaultModel, current.LiteLlm.DefaultModel),
                EmbeddingModel = NormalizeRequired(draft.LiteLlm.EmbeddingModel, current.LiteLlm.EmbeddingModel),
                ContextWindowTokens = draft.LiteLlm.ContextWindowTokens > 0
                    ? draft.LiteLlm.ContextWindowTokens
                    : current.LiteLlm.ContextWindowTokens
            },
            Qdrant = new QdrantConfig
            {
                Host = NormalizeRequired(draft.Qdrant.Host, current.Qdrant.Host),
                Port = draft.Qdrant.Port > 0 ? draft.Qdrant.Port : current.Qdrant.Port,
                CollectionName = NormalizeRequired(draft.Qdrant.CollectionName, current.Qdrant.CollectionName),
                EmbeddingDimension = draft.Qdrant.EmbeddingDimension > 0
                    ? draft.Qdrant.EmbeddingDimension
                    : current.Qdrant.EmbeddingDimension
            },
            Signal = new SignalConfig
            {
                CliPath = NormalizeRequired(draft.Signal.CliPath, current.Signal.CliPath),
                Account = (draft.Signal.Account ?? string.Empty).Trim(),
                Enabled = draft.Signal.Enabled
            },
            Wiki = new WikiConfig
            {
                BasePath = NormalizeRequired(draft.Wiki.BasePath, current.Wiki.BasePath),
                MaxFactsPerEntry = draft.Wiki.MaxFactsPerEntry > 0
                    ? draft.Wiki.MaxFactsPerEntry
                    : current.Wiki.MaxFactsPerEntry,
                StaleFactDays = draft.Wiki.StaleFactDays > 0
                    ? draft.Wiki.StaleFactDays
                    : current.Wiki.StaleFactDays,
                MinConfidenceThreshold = draft.Wiki.MinConfidenceThreshold > 0
                    ? draft.Wiki.MinConfidenceThreshold
                    : current.Wiki.MinConfidenceThreshold
            },
            Context = current.Context,
            Scheduler = new SchedulerConfig
            {
                Enabled = draft.Scheduler.Enabled,
                WikiMaintenanceCron = NormalizeRequired(
                    draft.Scheduler.WikiMaintenanceCron,
                    current.Scheduler.WikiMaintenanceCron)
            },
            Auth = current.Auth,
            Knowledge = current.Knowledge
        };

        return merged;
    }

    private static string NormalizeRequired(string? candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();

    private static string NormalizeUrl(string? candidate, string fallback)
    {
        var selected = NormalizeRequired(candidate, fallback);
        return selected.TrimEnd('/');
    }

    private static async Task<OnboardingStepResult> ValidateFilesystemAsync(LeanKernelConfig cfg, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(cfg.Wiki.BasePath);
            foreach (var dim in Enum.GetValues<WikiDimension>())
                Directory.CreateDirectory(Path.Combine(cfg.Wiki.BasePath, dim.ToString().ToLowerInvariant()));

            var dataDir = Path.GetDirectoryName(cfg.Wiki.BasePath) ?? "/app/data";
            Directory.CreateDirectory(Path.Combine(dataDir, "sessions"));
            Directory.CreateDirectory(Path.Combine(dataDir, "logs"));

            var probe = Path.Combine(cfg.Wiki.BasePath, ".onboarding-write-probe");
            await File.WriteAllTextAsync(probe, "ok", ct);
            File.Delete(probe);

            return new OnboardingStepResult
            {
                Step = "filesystem",
                Success = true,
                Message = "Wiki/data paths are writable."
            };
        }
        catch (Exception ex)
        {
            return new OnboardingStepResult
            {
                Step = "filesystem",
                Success = false,
                Message = $"Filesystem validation failed: {ex.Message}"
            };
        }
    }

    private async Task<OnboardingStepResult> ValidateLiteLlmModelsAsync(LeanKernelConfig cfg, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cfg.LiteLlm.BaseUrl))
        {
            return new OnboardingStepResult
            {
                Step = "litellm-models",
                Success = false,
                Message = "LiteLLM base URL is required."
            };
        }

        try
        {
            var client = _httpClientFactory.CreateClient("onboarding-probe");
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                BuildEndpoint(cfg.LiteLlm.BaseUrl, "v1/models"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.LiteLlm.ApiKey);

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return new OnboardingStepResult
                {
                    Step = "litellm-models",
                    Success = false,
                    Message = $"LiteLLM /v1/models returned {(int)response.StatusCode}."
                };
            }

            return new OnboardingStepResult
            {
                Step = "litellm-models",
                Success = true,
                Message = "LiteLLM model endpoint is reachable."
            };
        }
        catch (Exception ex)
        {
            return new OnboardingStepResult
            {
                Step = "litellm-models",
                Success = false,
                Message = $"LiteLLM connectivity failed: {ex.Message}"
            };
        }
    }

    private async Task<OnboardingStepResult> ValidateEmbeddingEndpointAsync(LeanKernelConfig cfg, CancellationToken ct)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                model = cfg.LiteLlm.EmbeddingModel,
                input = new[] { "onboarding readiness probe" }
            });

            var client = _httpClientFactory.CreateClient("onboarding-probe");
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                BuildEndpoint(cfg.LiteLlm.BaseUrl, "v1/embeddings"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.LiteLlm.ApiKey);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return new OnboardingStepResult
                {
                    Step = "embeddings",
                    Success = false,
                    Message = $"Embedding probe returned {(int)response.StatusCode}."
                };
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            {
                return new OnboardingStepResult
                {
                    Step = "embeddings",
                    Success = false,
                    Message = "Embedding probe returned no vectors."
                };
            }

            return new OnboardingStepResult
            {
                Step = "embeddings",
                Success = true,
                Message = "Embedding endpoint is operational."
            };
        }
        catch (Exception ex)
        {
            return new OnboardingStepResult
            {
                Step = "embeddings",
                Success = false,
                Message = $"Embedding probe failed: {ex.Message}"
            };
        }
    }

    private static async Task<OnboardingStepResult> ValidateQdrantAsync(LeanKernelConfig cfg, CancellationToken ct)
    {
        try
        {
            var client = new QdrantClient(cfg.Qdrant.Host, cfg.Qdrant.Port);
            var exists = await client.CollectionExistsAsync(cfg.Qdrant.CollectionName, ct);
            if (!exists)
            {
                await client.CreateCollectionAsync(
                    cfg.Qdrant.CollectionName,
                    new VectorParams
                    {
                        Size = (ulong)cfg.Qdrant.EmbeddingDimension,
                        Distance = Distance.Cosine
                    },
                    cancellationToken: ct);
            }

            return new OnboardingStepResult
            {
                Step = "qdrant",
                Success = true,
                Message = "Qdrant is reachable and collection is ready."
            };
        }
        catch (Exception ex)
        {
            return new OnboardingStepResult
            {
                Step = "qdrant",
                Success = false,
                Message = $"Qdrant validation failed: {ex.Message}"
            };
        }
    }

    private static OnboardingStepResult ValidateSignal(LeanKernelConfig cfg)
    {
        if (!cfg.Signal.Enabled)
        {
            return new OnboardingStepResult
            {
                Step = "signal",
                Success = true,
                Message = "Signal is disabled (optional)."
            };
        }

        if (string.IsNullOrWhiteSpace(cfg.Signal.Account))
        {
            return new OnboardingStepResult
            {
                Step = "signal",
                Success = false,
                Message = "Signal is enabled but no account is configured."
            };
        }

        if (!File.Exists(cfg.Signal.CliPath))
        {
            return new OnboardingStepResult
            {
                Step = "signal",
                Success = false,
                Message = $"Signal CLI not found at '{cfg.Signal.CliPath}'."
            };
        }

        return new OnboardingStepResult
        {
            Step = "signal",
            Success = true,
            Message = "Signal configuration looks valid."
        };
    }

    private static Uri BuildEndpoint(string baseUrl, string relativePath)
    {
        var normalizedBase = baseUrl.TrimEnd('/') + "/";
        return new Uri(new Uri(normalizedBase), relativePath);
    }
}

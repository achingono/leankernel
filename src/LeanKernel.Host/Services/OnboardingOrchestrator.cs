using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Net;
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
                Enabled = draft.Signal.Enabled,
                AllowedSenders = current.Signal.AllowedSenders?.ToArray() ?? [],
                DaemonBaseUrl = current.Signal.DaemonBaseUrl
            },
            Unstructured = current.Unstructured,
            Agents = current.Agents,
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
                    current.Scheduler.WikiMaintenanceCron),
                ChatFactScrubCron = NormalizeRequired(
                    draft.Scheduler.ChatFactScrubCron,
                    current.Scheduler.ChatFactScrubCron)
            },
            Auth = current.Auth,
            Knowledge = current.Knowledge,
            Routing = current.Routing,
            Engagement = current.Engagement,
            SignalPhoneNumber = current.SignalPhoneNumber,
            SignalServerUrl = current.SignalServerUrl,
            SignalApiToken = current.SignalApiToken,
            DiscordBotToken = current.DiscordBotToken,
            DiscordChannelId = current.DiscordChannelId
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
            var availableModels = await GetAvailableLiteLlmModelIdsAsync(cfg, ct);
            var orderedCandidates = BuildEmbeddingProbeCandidates(cfg, availableModels);
            if (orderedCandidates.Count == 0)
            {
                return new OnboardingStepResult
                {
                    Step = "embeddings",
                    Success = true,
                    Message = "No embedding model is currently configured in LiteLLM; skipping embedding probe."
                };
            }

            var client = _httpClientFactory.CreateClient("onboarding-probe");
            var endpoint = BuildEndpoint(cfg.LiteLlm.BaseUrl, "v1/embeddings");
            string? lastFailure = null;

            foreach (var model in orderedCandidates)
            {
                var probe = await ProbeEmbeddingModelAsync(
                    client,
                    endpoint,
                    cfg.LiteLlm.ApiKey,
                    model,
                    orderedCandidates.Count,
                    ct);

                if (probe.Result is not null)
                    return probe.Result;

                lastFailure = probe.LastFailure ?? lastFailure;
            }

            return new OnboardingStepResult
            {
                Step = "embeddings",
                Success = false,
                Message = lastFailure ?? "Embedding probe failed for all candidate models."
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

    private static List<string> BuildEmbeddingProbeCandidates(
        LeanKernelConfig cfg,
        HashSet<string> availableModels)
    {
        var probeCandidates = new HashSet<string>(StringComparer.Ordinal);
        var configuredEmbeddingModel = cfg.LiteLlm.EmbeddingModel?.Trim();

        if (!string.IsNullOrWhiteSpace(configuredEmbeddingModel) &&
            (availableModels.Count == 0 || availableModels.Contains(configuredEmbeddingModel)))
        {
            probeCandidates.Add(configuredEmbeddingModel);
        }

        foreach (var availableModel in availableModels.Where(IsLikelyEmbeddingModelId))
            probeCandidates.Add(availableModel);

        return probeCandidates.ToList();
    }

    private static async Task<EmbeddingProbeOutcome> ProbeEmbeddingModelAsync(
        HttpClient client,
        Uri endpoint,
        string apiKey,
        string model,
        int candidateCount,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model,
            input = new[] { "onboarding readiness probe" }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return await HandleFailedEmbeddingProbeAsync(response, model, candidateCount, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            return EmbeddingProbeOutcome.Failed($"Embedding probe for '{model}' returned no vectors.");

        return EmbeddingProbeOutcome.Completed(new OnboardingStepResult
        {
            Step = "embeddings",
            Success = true,
            Message = $"Embedding endpoint is operational (model: {model})."
        });
    }

    private static async Task<EmbeddingProbeOutcome> HandleFailedEmbeddingProbeAsync(
        HttpResponseMessage response,
        string model,
        int candidateCount,
        CancellationToken ct)
    {
        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var lastFailure = $"Embedding probe for '{model}' returned {(int)response.StatusCode}.";

        if (response.StatusCode == HttpStatusCode.BadRequest &&
            responseBody.Contains("Invalid model name", StringComparison.OrdinalIgnoreCase) &&
            candidateCount == 1)
        {
            return EmbeddingProbeOutcome.Completed(new OnboardingStepResult
            {
                Step = "embeddings",
                Success = true,
                Message = "Configured embedding model is unavailable in LiteLLM; skipping embedding probe."
            });
        }

        return EmbeddingProbeOutcome.Failed(lastFailure);
    }

    private async Task<HashSet<string>> GetAvailableLiteLlmModelIdsAsync(LeanKernelConfig cfg, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("onboarding-probe");
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildEndpoint(cfg.LiteLlm.BaseUrl, "v1/models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.LiteLlm.ApiKey);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return [];

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        var modelIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var model in data.EnumerateArray())
        {
            if (model.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
            {
                var id = idProp.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                    modelIds.Add(id.Trim());
            }
        }

        return modelIds;
    }

    private static bool IsLikelyEmbeddingModelId(string modelId) =>
        modelId.Contains("embedding", StringComparison.OrdinalIgnoreCase)
        || modelId.Contains("embed", StringComparison.OrdinalIgnoreCase);

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

        var resolvedCliPath = ResolveSignalCliPath(cfg.Signal.CliPath);
        if (resolvedCliPath is null)
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
            Message = $"Signal configuration looks valid (CLI: {resolvedCliPath})."
        };
    }

    private static string? ResolveSignalCliPath(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        foreach (var candidate in new[] { "/usr/bin/signal-cli", "/usr/local/bin/signal-cli" })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static Uri BuildEndpoint(string baseUrl, string relativePath)
    {
        var normalizedBase = baseUrl.TrimEnd('/') + "/";
        return new Uri(new Uri(normalizedBase), relativePath);
    }

    private sealed record EmbeddingProbeOutcome(OnboardingStepResult? Result, string? LastFailure)
    {
        public static EmbeddingProbeOutcome Completed(OnboardingStepResult result) => new(result, null);

        public static EmbeddingProbeOutcome Failed(string lastFailure) => new(null, lastFailure);
    }
}

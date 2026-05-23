using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Gateway.Services;

public sealed class DiagnosticsService : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<DiagnosticsService> _logger;

    public DiagnosticsService(
        NavigationManager navigationManager,
        IConfiguration configuration,
        ILogger<DiagnosticsService> logger)
    {
        ArgumentNullException.ThrowIfNull(navigationManager);
        ArgumentNullException.ThrowIfNull(configuration);

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(navigationManager.BaseUri, UriKind.Absolute)
        };

        var apiKey = ResolveGatewayApiKey(configuration);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", apiKey);
        }
    }

    public async Task<DiagnosticsLoadResult> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var normalizedSessionId = sessionId.Trim();
        var encodedSessionId = Uri.EscapeDataString(normalizedSessionId);

        var rawTask = GetAsync<RawDiagnosticsResponse>($"/api/diagnostics/{encodedSessionId}", ct);
        var contextTask = GetAsync<ContextDiagnosticsResponse>($"/api/diagnostics/{encodedSessionId}/context", ct);
        var budgetTask = GetAsync<BudgetDiagnosticsResponse>($"/api/diagnostics/{encodedSessionId}/budget", ct);
        var historyTask = GetAsync<HistoryDiagnosticsResponse>($"/api/diagnostics/{encodedSessionId}/history", ct);

        await Task.WhenAll(rawTask, contextTask, budgetTask, historyTask).ConfigureAwait(false);

        var rawResult = await rawTask.ConfigureAwait(false);
        var contextResult = await contextTask.ConfigureAwait(false);
        var budgetResult = await budgetTask.ConfigureAwait(false);
        var historyResult = await historyTask.ConfigureAwait(false);

        if (rawResult.IsUnauthorized || contextResult.IsUnauthorized || budgetResult.IsUnauthorized || historyResult.IsUnauthorized)
        {
            return new DiagnosticsLoadResult
            {
                ErrorMessage = "Diagnostics are protected by an API key. Check the Gateway configuration and try again."
            };
        }

        var warnings = new List<string>();
        AddWarningIfNeeded(rawResult, "The raw diagnostics feed could not be loaded.", warnings);
        AddWarningIfNeeded(contextResult, "Context audit data is unavailable for this session.", warnings);
        AddWarningIfNeeded(budgetResult, "Budget diagnostics are unavailable for this session.", warnings);
        AddWarningIfNeeded(historyResult, "History shaping diagnostics are unavailable for this session.", warnings);

        var rawEntries = rawResult.Value?.Entries ?? [];
        var turnId = contextResult.Value?.TurnId
            ?? budgetResult.Value?.TurnId
            ?? historyResult.Value?.TurnId;

        var routingDecision = SelectLatestPayload<RoutingDecision>(rawEntries, DiagnosticCategory.ModelRouting, turnId);
        var qualityGate = SelectLatestPayload<QualityGateResult>(rawEntries, DiagnosticCategory.QualityGate, turnId);
        var shadowRouting = SelectLatestPayload<ShadowRoutingResult>(rawEntries, DiagnosticCategory.Shadow, turnId);

        var hasAnyData = contextResult.Value is not null
            || budgetResult.Value is not null
            || historyResult.Value is not null
            || routingDecision is not null
            || qualityGate is not null
            || shadowRouting is not null
            || rawEntries.Count > 0;

        if (!hasAnyData)
        {
            var fatalError = rawResult.ErrorMessage
                ?? contextResult.ErrorMessage
                ?? budgetResult.ErrorMessage
                ?? historyResult.ErrorMessage;

            return new DiagnosticsLoadResult
            {
                ErrorMessage = string.IsNullOrWhiteSpace(fatalError)
                    ? $"No diagnostics were found for session '{normalizedSessionId}'."
                    : fatalError
            };
        }

        var timestamp = contextResult.Value?.Timestamp
            ?? rawEntries
                .OrderBy(entry => entry.Timestamp)
                .LastOrDefault()?
                .Timestamp;

        return new DiagnosticsLoadResult
        {
            Data = new DiagnosticsExplorerData
            {
                SessionId = normalizedSessionId,
                TurnId = turnId,
                Timestamp = timestamp,
                Context = contextResult.Value,
                Budget = budgetResult.Value,
                History = historyResult.Value,
                RoutingDecision = routingDecision,
                QualityGate = qualityGate,
                ShadowRouting = shadowRouting,
                RawEntryCount = rawEntries.Count,
                Warnings = warnings,
            }
        };
    }

    private async Task<ApiCallResult<T>> GetAsync<T>(string relativePath, CancellationToken ct)
        where T : class
    {
        try
        {
            using var response = await _httpClient.GetAsync(relativePath, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return ApiCallResult<T>.NotFound();
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return ApiCallResult<T>.Unauthorized();
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning(
                    "Diagnostics request to {Path} failed with {StatusCode}: {Body}",
                    relativePath,
                    (int)response.StatusCode,
                    errorBody);

                return ApiCallResult<T>.Failure($"The diagnostics request failed with status {(int)response.StatusCode}.");
            }

            var payload = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, ct).ConfigureAwait(false);
            return payload is null
                ? ApiCallResult<T>.Failure("The diagnostics response body was empty.")
                : ApiCallResult<T>.Success(payload);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Diagnostics request to {Path} could not be completed", relativePath);
            return ApiCallResult<T>.Failure("The diagnostics API could not be reached.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Diagnostics response from {Path} could not be parsed", relativePath);
            return ApiCallResult<T>.Failure("The diagnostics response could not be parsed.");
        }
    }

    private TModel? SelectLatestPayload<TModel>(
        IReadOnlyList<RawDiagnosticEntry> entries,
        DiagnosticCategory category,
        string? preferredTurnId)
        where TModel : class
    {
        var categoryName = category.ToString();

        var preferredEntries = entries
            .Where(entry => string.Equals(entry.Category, categoryName, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(preferredTurnId)
                && string.Equals(entry.TurnId, preferredTurnId, StringComparison.Ordinal))
            .OrderByDescending(entry => entry.Timestamp)
            .ToList();

        var fallbackEntries = entries
            .Where(entry => string.Equals(entry.Category, categoryName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Timestamp)
            .ToList();

        foreach (var entry in preferredEntries.Concat(fallbackEntries).DistinctBy(item => item.Id))
        {
            try
            {
                var payload = entry.Payload.Deserialize<TModel>(SerializerOptions);
                if (payload is not null)
                {
                    return payload;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(
                    ex,
                    "Skipping malformed diagnostics payload {DiagnosticEntryId} in category {Category}",
                    entry.Id,
                    entry.Category);
            }
        }

        return null;
    }

    private static void AddWarningIfNeeded<T>(ApiCallResult<T> result, string notFoundMessage, ICollection<string> warnings)
        where T : class
    {
        if (result.IsNotFound)
        {
            warnings.Add(notFoundMessage);
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            warnings.Add(result.ErrorMessage);
        }
    }

    private static string? ResolveGatewayApiKey(IConfiguration configuration)
    {
        var configuredKeys = configuration
            .GetSection("LeanKernel:Gateway:ApiKeys")
            .Get<string[]>()?
            .FirstOrDefault(key => !string.IsNullOrWhiteSpace(key));

        if (!string.IsNullOrWhiteSpace(configuredKeys))
        {
            return configuredKeys;
        }

        var singleKey = configuration.GetValue<string>("LeanKernel:Gateway:ApiKey");
        return string.IsNullOrWhiteSpace(singleKey)
            ? null
            : singleKey;
    }

    public void Dispose() => _httpClient.Dispose();

    public sealed record DiagnosticsLoadResult
    {
        public DiagnosticsExplorerData? Data { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public sealed record DiagnosticsExplorerData
    {
        public required string SessionId { get; init; }
        public string? TurnId { get; init; }
        public DateTimeOffset? Timestamp { get; init; }
        public ContextDiagnosticsResponse? Context { get; init; }
        public BudgetDiagnosticsResponse? Budget { get; init; }
        public HistoryDiagnosticsResponse? History { get; init; }
        public RoutingDecision? RoutingDecision { get; init; }
        public QualityGateResult? QualityGate { get; init; }
        public ShadowRoutingResult? ShadowRouting { get; init; }
        public int RawEntryCount { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = [];
    }

    private sealed record ApiCallResult<T>(T? Value, bool IsSuccess, bool IsNotFound, bool IsUnauthorized, string? ErrorMessage)
        where T : class
    {
        public static ApiCallResult<T> Success(T value) => new(value, true, false, false, null);

        public static ApiCallResult<T> NotFound() => new(null, false, true, false, null);

        public static ApiCallResult<T> Unauthorized() => new(null, false, false, true, null);

        public static ApiCallResult<T> Failure(string message) => new(null, false, false, false, message);
    }

    private sealed record RawDiagnosticsResponse
    {
        public IReadOnlyList<RawDiagnosticEntry> Entries { get; init; } = [];
        public int Count { get; init; }
        public string? Message { get; init; }
    }

    private sealed record RawDiagnosticEntry
    {
        public string Id { get; init; } = string.Empty;
        public string SessionId { get; init; } = string.Empty;
        public string? TurnId { get; init; }
        public string Category { get; init; } = string.Empty;
        public JsonElement Payload { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }
}

using System.Diagnostics.Metrics;
using LeanKernel.Abstractions.Models;

namespace LeanKernel.Diagnostics;

/// <summary>
/// OpenTelemetry metrics for LeanKernel.
/// </summary>
public sealed class LeanKernelMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _turnsProcessed;
    private readonly Counter<long> _tokensUsed;
    private readonly Histogram<double> _turnLatency;
    private readonly Counter<long> _qualityGateFailures;
    private readonly Counter<long> _escalations;
    private readonly Histogram<double> _budgetUtilization;
    private readonly Counter<long> _requestsTotal;
    private readonly Histogram<double> _requestsDuration;
    private readonly Counter<long> _requestErrors;
    private readonly Counter<long> _rateLimitRejected;
    private readonly ObservableGauge<double> _spendTotalUsd;
    private readonly ObservableGauge<int> _providersHealth;
    private readonly object _sync = new();
    private decimal _dailySpendUsd;
    private decimal _monthlySpendUsd;
    private Dictionary<string, int> _providerHealth = new(StringComparer.OrdinalIgnoreCase)
    {
        [ProviderNames.Database] = 1,
        [ProviderNames.LiteLlm] = 1,
        [ProviderNames.GBrain] = 1,
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="LeanKernelMetrics"/> class.
    /// </summary>
    public LeanKernelMetrics()
    {
        _meter = new Meter("LeanKernel", "1.0.0");
        _turnsProcessed = _meter.CreateCounter<long>("leankernel.turns.processed", "turns", "Total turns processed");
        _tokensUsed = _meter.CreateCounter<long>("leankernel.tokens.used", "tokens", "Total tokens consumed");
        _turnLatency = _meter.CreateHistogram<double>("leankernel.turn.latency", "ms", "Turn processing latency");
        _qualityGateFailures = _meter.CreateCounter<long>("leankernel.quality_gate.failures", "failures", "Quality gate failures");
        _escalations = _meter.CreateCounter<long>("leankernel.escalations", "escalations", "Model escalations");
        _budgetUtilization = _meter.CreateHistogram<double>("leankernel.budget.utilization", "ratio", "Budget utilization ratio");
        _requestsTotal = _meter.CreateCounter<long>("leankernel.requests.total", "requests", "Total HTTP requests");
        _requestsDuration = _meter.CreateHistogram<double>("leankernel.requests.duration", "ms", "HTTP request duration");
        _requestErrors = _meter.CreateCounter<long>("leankernel.requests.errors", "errors", "HTTP request errors");
        _rateLimitRejected = _meter.CreateCounter<long>("leankernel.ratelimit.rejected", "requests", "Rate-limited requests");
        _spendTotalUsd = _meter.CreateObservableGauge("leankernel.spend.total_usd", ObserveSpendTotalUsd, "usd", "Current tracked spend totals");
        _providersHealth = _meter.CreateObservableGauge("leankernel.providers.health", ObserveProviderHealth, description: "Provider health (1=healthy, 0=unhealthy)");
    }

    /// <summary>
    /// Records a processed turn.
    /// </summary>
    /// <param name="model">The model used for the turn.</param>
    public void RecordTurnProcessed(string model) =>
        _turnsProcessed.Add(1, new KeyValuePair<string, object?>("model", model));

    /// <summary>
    /// Records token usage.
    /// </summary>
    /// <param name="tokens">The token count.</param>
    /// <param name="model">The model used for the turn.</param>
    public void RecordTokensUsed(long tokens, string model) =>
        _tokensUsed.Add(tokens, new KeyValuePair<string, object?>("model", model));

    /// <summary>
    /// Records turn latency.
    /// </summary>
    /// <param name="milliseconds">The elapsed milliseconds.</param>
    public void RecordTurnLatency(double milliseconds) => _turnLatency.Record(milliseconds);

    /// <summary>
    /// Records a quality-gate failure.
    /// </summary>
    /// <param name="reason">The failure reason.</param>
    public void RecordQualityGateFailure(string reason) =>
        _qualityGateFailures.Add(1, new KeyValuePair<string, object?>("reason", reason));

    /// <summary>
    /// Records a model escalation.
    /// </summary>
    /// <param name="fromModel">The source model.</param>
    /// <param name="toModel">The destination model.</param>
    public void RecordEscalation(string fromModel, string toModel) =>
        _escalations.Add(
            1,
            new KeyValuePair<string, object?>("from", fromModel),
            new KeyValuePair<string, object?>("to", toModel));

    /// <summary>
    /// Records budget utilization.
    /// </summary>
    /// <param name="ratio">The utilization ratio.</param>
    public void RecordBudgetUtilization(double ratio) => _budgetUtilization.Record(ratio);

    /// <summary>
    /// Records an HTTP request.
    /// </summary>
    /// <param name="endpoint">The endpoint name or path.</param>
    /// <param name="method">The HTTP method.</param>
    public void RecordRequestTotal(string endpoint, string method) =>
        _requestsTotal.Add(
            1,
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("method", method));

    /// <summary>
    /// Records HTTP request duration.
    /// </summary>
    /// <param name="endpoint">The endpoint name or path.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="milliseconds">The elapsed milliseconds.</param>
    public void RecordRequestDuration(string endpoint, string method, int statusCode, double milliseconds) =>
        _requestsDuration.Record(
            milliseconds,
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("method", method),
            new KeyValuePair<string, object?>("status_code", statusCode));

    /// <summary>
    /// Records an HTTP request error.
    /// </summary>
    /// <param name="endpoint">The endpoint name or path.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    public void RecordRequestError(string endpoint, string method, int statusCode) =>
        _requestErrors.Add(
            1,
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("method", method),
            new KeyValuePair<string, object?>("status_code", statusCode));

    /// <summary>
    /// Records a rate-limit rejection.
    /// </summary>
    /// <param name="partitionKey">The rate-limit partition key.</param>
    public void RecordRateLimitRejected(string partitionKey) =>
        _rateLimitRejected.Add(1, new KeyValuePair<string, object?>("partition", partitionKey));

    /// <summary>
    /// Updates observable spend totals.
    /// </summary>
    /// <param name="dailySpendUsd">The current daily spend in USD.</param>
    /// <param name="monthlySpendUsd">The current monthly spend in USD.</param>
    public void SetSpendTotals(decimal dailySpendUsd, decimal monthlySpendUsd)
    {
        lock (_sync)
        {
            _dailySpendUsd = dailySpendUsd;
            _monthlySpendUsd = monthlySpendUsd;
        }
    }

    /// <summary>
    /// Updates observable provider health values.
    /// </summary>
    /// <param name="providers">The provider-health map.</param>
    public void SetProviderHealth(IReadOnlyDictionary<string, ProviderHealthStatus> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        lock (_sync)
        {
            _providerHealth = providers.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.IsHealthy ? 1 : 0,
                StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<double>> ObserveSpendTotalUsd()
    {
        lock (_sync)
        {
            return
            [
                new Measurement<double>((double)_dailySpendUsd, [new KeyValuePair<string, object?>("scope", "daily")]),
                new Measurement<double>((double)_monthlySpendUsd, [new KeyValuePair<string, object?>("scope", "monthly")]),
            ];
        }
    }

    private IEnumerable<Measurement<int>> ObserveProviderHealth()
    {
        lock (_sync)
        {
            return _providerHealth
                .Select(pair => new Measurement<int>(pair.Value, [new KeyValuePair<string, object?>("provider", pair.Key)]))
                .ToArray();
        }
    }
}

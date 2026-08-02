namespace LeanKernel.Logic.Telemetry;

/// <summary>
/// Scoped service that accumulates telemetry across multiple chat calls per turn.
/// <see cref="Capture"/> is called by the chat client decorator;
/// <see cref="Consume"/> is called by the persistence layer when storing the assistant turn.
/// </summary>
public sealed class TurnTelemetryCollector : ITurnTelemetryCollector
{
    private int _promptTokens;
    private int _completionTokens;
    private int _totalTokens;
    private decimal _responseCost;
    private double _latencyMs;

    private TurnTelemetry? _latest;

    /// <inheritdoc />
    public void Capture(TurnTelemetry telemetry)
    {
        _latest = telemetry;

        if (telemetry.Latency.HasValue)
        {
            _latencyMs += telemetry.Latency.Value.TotalMilliseconds;
        }

        if (telemetry.PromptTokens.HasValue)
        {
            _promptTokens += telemetry.PromptTokens.Value;
        }

        if (telemetry.CompletionTokens.HasValue)
        {
            _completionTokens += telemetry.CompletionTokens.Value;
        }

        if (telemetry.TotalTokens.HasValue)
        {
            _totalTokens += telemetry.TotalTokens.Value;
        }

        if (telemetry.ResponseCost.HasValue)
        {
            _responseCost += telemetry.ResponseCost.Value;
        }
    }

    /// <inheritdoc />
    public TurnTelemetry? Consume()
    {
        var latest = _latest;
        if (latest is null)
        {
            return null;
        }

        var accumulated = new TurnTelemetry
        {
            RequestedModel = latest.RequestedModel,
            ServedModel = latest.ServedModel,
            Provider = latest.Provider,
            ModelId = latest.ModelId,
            ApiBase = latest.ApiBase,
            PromptTokens = _promptTokens > 0 ? _promptTokens : latest.PromptTokens,
            CompletionTokens = _completionTokens > 0 ? _completionTokens : latest.CompletionTokens,
            TotalTokens = _totalTokens > 0 ? _totalTokens : latest.TotalTokens,
            ResponseCost = _responseCost > 0 ? _responseCost : latest.ResponseCost,
            Currency = latest.Currency,
            CostIsEstimated = latest.CostIsEstimated,
            Latency = _latencyMs > 0 ? TimeSpan.FromMilliseconds(_latencyMs) : latest.Latency,
            CapturedAt = latest.CapturedAt,
            SchemaVersion = latest.SchemaVersion,
            EvidenceClass = latest.EvidenceClass,
            GroundingStatus = latest.GroundingStatus,
            RetrievedMemoryKeys = latest.RetrievedMemoryKeys,
            RetrievedEvidenceClasses = latest.RetrievedEvidenceClasses,
        };

        _latest = null;
        _promptTokens = 0;
        _completionTokens = 0;
        _totalTokens = 0;
        _responseCost = 0;
        _latencyMs = 0;

        return accumulated;
    }
}
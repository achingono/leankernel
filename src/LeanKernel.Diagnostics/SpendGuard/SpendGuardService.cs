using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Diagnostics.SpendGuard;

/// <summary>
/// Enforces node-local spend guardrails using tier-based token cost estimates.
/// </summary>
public sealed class SpendGuardService(
    IOptions<HardeningConfig> config,
    ISpendTracker spendTracker,
    ILogger<SpendGuardService> logger) : ISpendGuardService
{
    private static readonly IReadOnlyDictionary<ModelTier, CostRate> CostRates = new Dictionary<ModelTier, CostRate>
    {
        [ModelTier.Economy] = new(0.00000015m, 0.00000060m),
        [ModelTier.Standard] = new(0.00000250m, 0.00001000m),
        [ModelTier.Premium] = new(0.00001500m, 0.00006000m),
    };

    private readonly HardeningConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
    private readonly ISpendTracker _spendTracker = spendTracker ?? throw new ArgumentNullException(nameof(spendTracker));
    private readonly ILogger<SpendGuardService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public decimal EstimateCostUsd(ModelTier tier, int inputTokens, int outputTokens)
    {
        var rates = CostRates.TryGetValue(tier, out var configuredRate)
            ? configuredRate
            : CostRates[ModelTier.Standard];

        return Math.Round(
            Math.Max(0, inputTokens) * rates.InputCostPerTokenUsd + Math.Max(0, outputTokens) * rates.OutputCostPerTokenUsd,
            6,
            MidpointRounding.AwayFromZero);
    }

    /// <inheritdoc />
    public SpendGuardDecision Evaluate(
        string sessionId,
        ModelTier tier,
        int estimatedInputTokens,
        int estimatedOutputTokens,
        DateTimeOffset? asOf = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var spendConfig = _config.SpendGuard;
        var estimatedCostUsd = EstimateCostUsd(tier, estimatedInputTokens, estimatedOutputTokens);
        var snapshot = _spendTracker.GetSnapshot(asOf);
        var projectedDailySpendUsd = snapshot.DailyTotalUsd + estimatedCostUsd;
        var projectedSessionSpendUsd = snapshot.GetSessionSpendUsd(sessionId) + estimatedCostUsd;
        var projectedMonthlySpendUsd = snapshot.MonthlyTotalUsd + estimatedCostUsd;
        var warningThresholdPercent = ResolveWarningThresholdPercent(spendConfig.WarnAtPercent);

        if (!spendConfig.Enabled)
        {
            return CreateDecision(
                SpendGuardAction.Allow,
                "Spend guard is disabled.",
                estimatedCostUsd,
                projectedDailySpendUsd,
                projectedSessionSpendUsd,
                projectedMonthlySpendUsd,
                warningThresholdPercent);
        }

        if (projectedSessionSpendUsd > spendConfig.MaxSessionSpendUsd)
        {
            _logger.LogWarning("Spend guard blocked request for session {SessionId} because the session limit would be exceeded", sessionId);
            return CreateDecision(
                SpendGuardAction.Block,
                $"This request was blocked because it would exceed the session spend limit of ${spendConfig.MaxSessionSpendUsd:0.00}.",
                estimatedCostUsd,
                projectedDailySpendUsd,
                projectedSessionSpendUsd,
                projectedMonthlySpendUsd,
                warningThresholdPercent);
        }

        if (projectedDailySpendUsd > spendConfig.MaxDailySpendUsd)
        {
            _logger.LogWarning("Spend guard blocked request for session {SessionId} because the daily limit would be exceeded", sessionId);
            return CreateDecision(
                SpendGuardAction.Block,
                $"This request was blocked because it would exceed the daily spend limit of ${spendConfig.MaxDailySpendUsd:0.00}.",
                estimatedCostUsd,
                projectedDailySpendUsd,
                projectedSessionSpendUsd,
                projectedMonthlySpendUsd,
                warningThresholdPercent);
        }

        if (projectedMonthlySpendUsd > spendConfig.MaxMonthlySpendUsd)
        {
            _logger.LogWarning("Spend guard blocked request for session {SessionId} because the monthly limit would be exceeded", sessionId);
            return CreateDecision(
                SpendGuardAction.Block,
                $"This request was blocked because it would exceed the monthly spend limit of ${spendConfig.MaxMonthlySpendUsd:0.00}.",
                estimatedCostUsd,
                projectedDailySpendUsd,
                projectedSessionSpendUsd,
                projectedMonthlySpendUsd,
                warningThresholdPercent);
        }

        var exceedsWarningThreshold = IsOverWarningThreshold(projectedSessionSpendUsd, spendConfig.MaxSessionSpendUsd, warningThresholdPercent)
            || IsOverWarningThreshold(projectedDailySpendUsd, spendConfig.MaxDailySpendUsd, warningThresholdPercent)
            || IsOverWarningThreshold(projectedMonthlySpendUsd, spendConfig.MaxMonthlySpendUsd, warningThresholdPercent);

        return exceedsWarningThreshold
            ? CreateDecision(
                SpendGuardAction.Warn,
                $"This request is approaching configured spend limits ({warningThresholdPercent:0.#}% warning threshold).",
                estimatedCostUsd,
                projectedDailySpendUsd,
                projectedSessionSpendUsd,
                projectedMonthlySpendUsd,
                warningThresholdPercent)
            : CreateDecision(
                SpendGuardAction.Allow,
                "Spend guard allowed the request.",
                estimatedCostUsd,
                projectedDailySpendUsd,
                projectedSessionSpendUsd,
                projectedMonthlySpendUsd,
                warningThresholdPercent);
    }

    private SpendGuardDecision CreateDecision(
        SpendGuardAction action,
        string reason,
        decimal estimatedCostUsd,
        decimal projectedDailySpendUsd,
        decimal projectedSessionSpendUsd,
        decimal projectedMonthlySpendUsd,
        decimal warningThresholdPercent)
        => new()
        {
            Action = action,
            Reason = reason,
            EstimatedCostUsd = estimatedCostUsd,
            DailySpendUsd = projectedDailySpendUsd,
            SessionSpendUsd = projectedSessionSpendUsd,
            MonthlySpendUsd = projectedMonthlySpendUsd,
            DailyLimitUsd = _config.SpendGuard.MaxDailySpendUsd,
            SessionLimitUsd = _config.SpendGuard.MaxSessionSpendUsd,
            MonthlyLimitUsd = _config.SpendGuard.MaxMonthlySpendUsd,
            WarningThresholdPercent = warningThresholdPercent,
        };

    private static bool IsOverWarningThreshold(decimal currentValue, decimal limit, decimal thresholdPercent)
        => limit > 0m && currentValue / limit >= thresholdPercent / 100m;

    private static decimal ResolveWarningThresholdPercent(string value)
        => decimal.TryParse(value, out var parsedValue) && parsedValue is > 0m and <= 100m
            ? parsedValue
            : 80m;

    private readonly record struct CostRate(decimal InputCostPerTokenUsd, decimal OutputCostPerTokenUsd);
}

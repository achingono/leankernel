namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configures production-hardening features for LeanKernel.
/// </summary>
public sealed class HardeningConfig
{
    /// <summary>
    /// Gets or sets spend-guard configuration.
    /// </summary>
    public SpendGuardConfig SpendGuard { get; set; } = new();

    /// <summary>
    /// Gets or sets gateway rate-limit configuration.
    /// </summary>
    public RateLimitConfig RateLimit { get; set; } = new();

    /// <summary>
    /// Gets or sets provider-health tracking configuration.
    /// </summary>
    public HealthTrackingConfig HealthTracking { get; set; } = new();

    /// <summary>
    /// Gets or sets resilience configuration.
    /// </summary>
    public ResilienceConfig Resilience { get; set; } = new();
}

/// <summary>
/// Configures spend-guard limits.
/// </summary>
public sealed class SpendGuardConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether spend guarding is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum daily spend in USD.
    /// </summary>
    public decimal MaxDailySpendUsd { get; set; } = 10.0m;

    /// <summary>
    /// Gets or sets the maximum per-session spend in USD.
    /// </summary>
    public decimal MaxSessionSpendUsd { get; set; } = 2.0m;

    /// <summary>
    /// Gets or sets the maximum monthly spend in USD.
    /// </summary>
    public decimal MaxMonthlySpendUsd { get; set; } = 100.0m;

    /// <summary>
    /// Gets or sets the warning threshold percentage.
    /// </summary>
    public string WarnAtPercent { get; set; } = "80";
}

/// <summary>
/// Configures gateway request rate limits.
/// </summary>
public sealed class RateLimitConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether request rate limiting is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the allowed request count per minute.
    /// </summary>
    public int RequestsPerMinute { get; set; } = 30;

    /// <summary>
    /// Gets or sets the allowed request count per hour.
    /// </summary>
    public int RequestsPerHour { get; set; } = 300;

    /// <summary>
    /// Gets or sets the maximum concurrent request count per caller.
    /// </summary>
    public int ConcurrentRequests { get; set; } = 5;
}

/// <summary>
/// Configures provider-health transition thresholds.
/// </summary>
public sealed class HealthTrackingConfig
{
    /// <summary>
    /// Gets or sets the background health-check interval in seconds.
    /// </summary>
    public int CheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the consecutive failure threshold before a provider becomes unhealthy.
    /// </summary>
    public int UnhealthyThreshold { get; set; } = 3;

    /// <summary>
    /// Gets or sets the consecutive success threshold before a provider becomes healthy again.
    /// </summary>
    public int HealthyThreshold { get; set; } = 2;
}

/// <summary>
/// Configures resilience retry and timeout behavior.
/// </summary>
public sealed class ResilienceConfig
{
    /// <summary>
    /// Gets or sets the retry count for resilient operations.
    /// </summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>
    /// Gets or sets the retry delay in milliseconds.
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the circuit-breaker failure threshold.
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>
    /// Gets or sets the circuit-breaker open duration in seconds.
    /// </summary>
    public int CircuitBreakerDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the timeout in seconds for resilient operations.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

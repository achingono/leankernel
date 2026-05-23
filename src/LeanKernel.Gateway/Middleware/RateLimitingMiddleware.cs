using LeanKernel.Abstractions.Configuration;
using LeanKernel.Diagnostics;
using Microsoft.Extensions.Options;

namespace LeanKernel.Gateway.Middleware;

/// <summary>
/// Enforces per-caller sliding-window and concurrency request limits.
/// </summary>
public sealed class RateLimitingMiddleware(
    RequestDelegate next,
    IOptions<HardeningConfig> config,
    LeanKernelMetrics metrics,
    ILogger<RateLimitingMiddleware> logger,
    TimeProvider? timeProvider = null)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly HardeningConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value;
    private readonly LeanKernelMetrics _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    private readonly ILogger<RateLimitingMiddleware> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, RateLimitBucket> _buckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <returns>A task that completes when the request finishes.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_config.RateLimit.Enabled || IsExemptPath(context.Request.Path))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var key = ResolvePartitionKey(context);
        var bucket = GetBucket(key);
        var now = _timeProvider.GetUtcNow();

        var rejectForWindow = false;
        lock (bucket.SyncRoot)
        {
            Prune(bucket.MinuteWindow, now, TimeSpan.FromMinutes(1));
            Prune(bucket.HourWindow, now, TimeSpan.FromHours(1));

            if (bucket.MinuteWindow.Count >= Math.Max(1, _config.RateLimit.RequestsPerMinute)
                || bucket.HourWindow.Count >= Math.Max(1, _config.RateLimit.RequestsPerHour))
            {
                rejectForWindow = true;
            }
            else
            {
                bucket.MinuteWindow.Enqueue(now);
                bucket.HourWindow.Enqueue(now);
            }
        }

        if (rejectForWindow)
        {
            await RejectAsync(context, key, "Sliding window rate limit exceeded.").ConfigureAwait(false);
            return;
        }

        if (!await bucket.ConcurrentLimiter.WaitAsync(0, context.RequestAborted).ConfigureAwait(false))
        {
            await RejectAsync(context, key, "Concurrent request limit exceeded.").ConfigureAwait(false);
            return;
        }

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            bucket.ConcurrentLimiter.Release();
        }
    }

    private static bool IsExemptPath(PathString path)
        => path.StartsWithSegments("/api/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/healthz", StringComparison.OrdinalIgnoreCase);

    private RateLimitBucket GetBucket(string key)
    {
        lock (_sync)
        {
            if (_buckets.TryGetValue(key, out var existingBucket))
            {
                return existingBucket;
            }

            var createdBucket = new RateLimitBucket(Math.Max(1, _config.RateLimit.ConcurrentRequests));
            _buckets[key] = createdBucket;
            return createdBucket;
        }
    }

    private async Task RejectAsync(HttpContext context, string key, string reason)
    {
        _metrics.RecordRateLimitRejected(key);
        _logger.LogWarning("Rejecting request for rate-limit partition {PartitionKey}: {Reason}", key, reason);
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Too Many Requests",
            reason,
        }).ConfigureAwait(false);
    }

    private static void Prune(Queue<DateTimeOffset> queue, DateTimeOffset now, TimeSpan window)
    {
        while (queue.Count > 0 && now - queue.Peek() >= window)
        {
            queue.Dequeue();
        }
    }

    private static string ResolvePartitionKey(HttpContext context)
    {
        var apiKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return $"api-key:{apiKey}";
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString();
        return !string.IsNullOrWhiteSpace(remoteIp)
            ? $"ip:{remoteIp}"
            : "anonymous";
    }

    private sealed class RateLimitBucket
    {
        public RateLimitBucket(int concurrentLimit)
        {
            ConcurrentLimiter = new SemaphoreSlim(concurrentLimit, concurrentLimit);
        }

        public object SyncRoot { get; } = new();

        public Queue<DateTimeOffset> MinuteWindow { get; } = new();

        public Queue<DateTimeOffset> HourWindow { get; } = new();

        public SemaphoreSlim ConcurrentLimiter { get; }
    }
}

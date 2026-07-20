using LeanKernel.Services.Common.Scheduler;

namespace LeanKernel.Services.Learning.Scheduler;

/// <summary>
/// Dispatches scheduled jobs to the registered type-specific handlers.
/// </summary>
/// <param name="handlers">Registered scheduled job handlers.</param>
/// <param name="logger">Logger instance.</param>
public sealed class ScheduledJobExecutor(
    IEnumerable<IScheduledJobHandler> handlers,
    ILogger<ScheduledJobExecutor> logger) : IScheduledJobExecutor
{
    private readonly IReadOnlyDictionary<string, IScheduledJobHandler> _handlers = handlers
        .GroupBy(static handler => handler.JobType, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.OrdinalIgnoreCase);

    public async Task ExecuteAsync(ScheduledJobDefinition job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!_handlers.TryGetValue(job.JobType, out var handler))
        {
            var supportedTypes = string.Join(", ", _handlers.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase));
            throw new InvalidOperationException(
                $"No scheduled job handler is registered for job type '{job.JobType}'. Supported types: {supportedTypes}.");
        }

        var payload = ParsePayload(job);
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation("Executing scheduled job {JobName} ({JobType}).", job.Name, job.JobType);
        await handler.ExecuteAsync(job, payload, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Executed scheduled job {JobName} ({JobType}) in {ElapsedMs}ms.",
            job.Name,
            job.JobType,
            stopwatch.ElapsedMilliseconds);
    }

    private static JsonElement? ParsePayload(ScheduledJobDefinition job)
    {
        if (string.IsNullOrWhiteSpace(job.Payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(job.Payload);
            return document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Scheduled job '{job.Name}' ({job.JobType}) has invalid JSON payload.",
                ex);
        }
    }
}
using System.Diagnostics;
using System.Text.Json;

    /// <inheritdoc />

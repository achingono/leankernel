using LeanKernel.Data;
using LeanKernel.Services.Gateway.Configuration;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeanKernel.Services.Gateway.Providers;

/// <summary>
/// Background service that periodically purges expired event spine records from the database.
/// </summary>
public sealed class EventRetentionHostedService(
    IServiceProvider serviceProvider,
    IOptions<EventRetentionSettings> settings,
    ILogger<EventRetentionHostedService> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Event retention cleanup failed.");
            }

            await Task.Delay(
                TimeSpan.FromMinutes(settings.Value.CleanupIntervalMinutes),
                stoppingToken);
        }
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EntityContext>();

        var cutoff = DateTime.UtcNow.AddDays(-settings.Value.RetentionDays);
        var count = await dbContext.Events
            .Where(e => e.CreatedOn < cutoff)
            .ExecuteDeleteAsync(ct);

        if (count > 0)
        {
            logger.LogInformation("Purged {Count} event records older than {Cutoff}.", count, cutoff);
        }
    }
}
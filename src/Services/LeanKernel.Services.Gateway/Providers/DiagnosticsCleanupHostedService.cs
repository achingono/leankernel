namespace LeanKernel.Services.Gateway.Providers;

using LeanKernel.Data;
using LeanKernel.Services.Gateway.Configuration;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>
/// Background service that periodically purges expired diagnostic entries from the database.
/// </summary>
public sealed class DiagnosticsCleanupHostedService(
    IServiceProvider serviceProvider,
    IOptions<DiagnosticsSettings> settings,
    ILogger<DiagnosticsCleanupHostedService> logger) : BackgroundService
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
                logger.LogError(ex, "Diagnostics cleanup failed.");
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

        var cutoff = DateTimeOffset.UtcNow.AddDays(-settings.Value.RetentionDays);
        var entries = await dbContext.DiagnosticEntries
            .Where(e => e.CapturedAt < cutoff)
            .ToListAsync(ct);

        if (entries.Count > 0)
        {
            dbContext.DiagnosticEntries.RemoveRange(entries);
            await dbContext.SaveChangesAsync(ct);
            logger.LogInformation("Purged {Count} diagnostic entries older than {Cutoff}.", entries.Count, cutoff);
        }
    }
}

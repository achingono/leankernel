using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Persistence.Health;

/// <summary>
/// Probes PostgreSQL connectivity for provider-health tracking.
/// </summary>
public sealed class DatabaseHealthProbe(
    IDbContextFactory<LeanKernelDbContext> dbContextFactory,
    ILogger<DatabaseHealthProbe> logger) : IProviderHealthProbe
{
    private readonly IDbContextFactory<LeanKernelDbContext> _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    private readonly ILogger<DatabaseHealthProbe> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public string ProviderName => ProviderNames.Database;

    /// <inheritdoc />
    public async Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var canConnect = await dbContext.Database.CanConnectAsync(ct).ConfigureAwait(false);
            return canConnect
                ? ProviderProbeResult.Healthy("Database connectivity probe succeeded.")
                : ProviderProbeResult.Unhealthy("Database connectivity probe returned false.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database health probe failed");
            return ProviderProbeResult.Unhealthy("Database connectivity probe failed.", ex.Message);
        }
    }
}

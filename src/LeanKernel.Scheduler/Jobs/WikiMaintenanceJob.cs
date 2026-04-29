using Microsoft.Extensions.Logging;
using LeanKernel.Archivist.Wiki;

namespace LeanKernel.Scheduler.Jobs;

/// <summary>
/// Periodic wiki maintenance: compiles facts, prunes stale entries,
/// deduplicates, and re-indexes. Runs via cron (default: 3 AM daily).
/// </summary>
public sealed class WikiMaintenanceJob
{
    private readonly WikiCompiler _compiler;
    private readonly ILogger<WikiMaintenanceJob> _logger;

    public WikiMaintenanceJob(WikiCompiler compiler, ILogger<WikiMaintenanceJob> logger)
    {
        _compiler = compiler;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Wiki maintenance job starting...");
        await _compiler.CompileAsync(ct);
        _logger.LogInformation("Wiki maintenance job completed");
    }
}

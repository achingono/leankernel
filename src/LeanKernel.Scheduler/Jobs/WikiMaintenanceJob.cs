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

    /// <summary>
    /// Initializes a new instance of the <see cref="WikiMaintenanceJob" /> class.
    /// </summary>
    /// <param name="compiler">The compiler.</param>
    /// <param name="logger">The logger.</param>
    public WikiMaintenanceJob(WikiCompiler compiler, ILogger<WikiMaintenanceJob> logger)
    {
        _compiler = compiler;
        _logger = logger;
    }

    /// <summary>
    /// Executes the execute async operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Wiki maintenance job starting...");
        await _compiler.CompileAsync(ct);
        _logger.LogInformation("Wiki maintenance job completed");
    }
}

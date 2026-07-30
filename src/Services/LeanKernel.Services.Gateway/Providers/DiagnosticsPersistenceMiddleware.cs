namespace LeanKernel.Services.Gateway.Providers;

using LeanKernel.Data;
using LeanKernel.Logic.Diagnostics;

/// <summary>
/// Middleware that persists buffered diagnostic entries to the database after the request completes.
/// </summary>
/// <param name="next">The next middleware in the pipeline.</param>
public sealed class DiagnosticsPersistenceMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Invokes the middleware, persisting any buffered diagnostic entries after the downstream pipeline completes.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="collector">The diagnostics collector from which entries are consumed.</param>
    /// <param name="dbContext">The EF Core database context used for persistence.</param>
    /// <param name="logger">The logger instance.</param>
    /// <returns>A task that represents the completion of request processing and persistence.</returns>
    public async Task InvokeAsync(
        HttpContext context,
        IDiagnosticsCollector collector,
        EntityContext dbContext,
        ILogger<DiagnosticsPersistenceMiddleware> logger)
    {
        await next(context);

        try
        {
            var entries = collector.Consume();
            if (entries.Count > 0)
            {
                dbContext.DiagnosticEntries.AddRange(entries);
                await dbContext.SaveChangesAsync();
                logger.LogDebug("Persisted {Count} diagnostic entries.", entries.Count);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to persist diagnostic entries after request completed.");
        }
    }
}
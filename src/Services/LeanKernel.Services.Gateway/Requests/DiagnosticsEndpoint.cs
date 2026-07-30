namespace LeanKernel.Services.Gateway.Requests;

using LeanKernel.Data;
using LeanKernel.Logic.Diagnostics;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Maps diagnostics-related HTTP endpoints for retrieving entries and health status.
/// </summary>
public static class DiagnosticsEndpoint
{
    /// <summary>
    /// Maps the diagnostics endpoints (<c>/v1/diagnostics/entries</c> and <c>/v1/diagnostics/health</c>).
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    public static void MapDiagnosticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/diagnostics/entries", HandleListEntriesAsync).RequireAuthorization("AdminOnly");
        endpoints.MapGet("/v1/diagnostics/health", HandleHealthAsync).RequireAuthorization("AdminOnly");
    }

    /// <summary>
    /// Handles requests to list recent diagnostic entries.
    /// </summary>
    /// <param name="dbContext">The EF Core database context.</param>
    /// <param name="take">The maximum number of entries to return (clamped between 1 and 500).</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A JSON array of diagnostic entries ordered by capture time descending.</returns>
    internal static async Task<IResult> HandleListEntriesAsync(
        [FromServices] EntityContext dbContext,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        var limit = take <= 0 ? 100 : Math.Min(take, 500);
        var entries = await dbContext.DiagnosticEntries
            .AsNoTracking()
            .OrderByDescending(entry => entry.CapturedAt)
            .Take(limit)
            .Select(entry => new
            {
                entry.Id,
                entry.CorrelationId,
                entry.TurnId,
                entry.Source,
                entry.Category,
                entry.PayloadJson,
                entry.CapturedAt,
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(entries);
    }

    /// <summary>
    /// Handles requests to check the health of the system and its components.
    /// </summary>
    /// <param name="healthAggregator">The health aggregator service.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A JSON object with overall health and LiteLLM health status.</returns>
    internal static async Task<IResult> HandleHealthAsync(
        [FromServices] IHealthAggregator healthAggregator,
        CancellationToken cancellationToken = default)
    {
        var healthy = await healthAggregator.IsHealthyAsync(cancellationToken);
        var litellmHealthy = await healthAggregator.IsLiteLlmHealthyAsync(cancellationToken);

        return Results.Ok(new
        {
            healthy,
            litellmHealthy,
        });
    }
}
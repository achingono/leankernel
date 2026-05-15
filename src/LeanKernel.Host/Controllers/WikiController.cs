using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Host.Controllers;

/// <summary>
/// Represents the wiki controller.
/// </summary>
[ApiController]
[Route("api/wiki")]
[Authorize(Policy = AuthConstants.PolicyAdminOnly)]
public sealed class WikiController : ControllerBase
{
    private readonly IWikiStore _wiki;
    private readonly IWikiMigrationService _migration;
    private readonly IWikiImportService _importService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WikiController" /> class.
    /// </summary>
    /// <param name="wiki">The wiki.</param>
    /// <param name="migration">The wiki migration service.</param>
    public WikiController(
        IWikiStore wiki,
        IWikiMigrationService migration,
        IWikiImportService importService)
    {
        _wiki = wiki;
        _migration = migration;
        _importService = importService;
    }

    /// <summary>
    /// Executes the get dimensions operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    [HttpGet("dimensions")]
    public async Task<IActionResult> GetDimensions(CancellationToken ct)
    {
        var summary = new List<object>();
        foreach (var dim in Enum.GetValues<WikiDimension>())
        {
            var entries = await _wiki.ListByDimensionAsync(dim, ct);
            summary.Add(new
            {
                dimension = dim.ToString().ToLowerInvariant(),
                count = entries.Count,
                totalFacts = entries.Sum(e => e.Facts.Count)
            });
        }
        return Ok(summary);
    }

    /// <summary>
    /// Represents the list entries.
    /// </summary>
    [HttpGet("entries")]
    public async Task<IActionResult> ListEntries(
        [FromQuery] string? dimension = null,
        [FromQuery] string? q = null,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(dimension) &&
            Enum.TryParse<WikiDimension>(dimension, true, out var dim))
        {
            var entries = await _wiki.ListByDimensionAsync(dim, ct);
            if (!string.IsNullOrEmpty(q))
            {
                entries = entries
                    .Where(e => e.Subject.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                e.Facts.Any(f => f.Claim.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
            }
            return Ok(entries);
        }

        // Search across all dimensions
        if (!string.IsNullOrEmpty(q))
        {
            var query = new Core.Models.WikiQuery { TextQuery = q };
            var results = await _wiki.QueryAsync(query, ct);
            return Ok(results);
        }

        // List all
        var all = new List<Core.Models.WikiEntry>();
        foreach (var d in Enum.GetValues<WikiDimension>())
        {
            var entries = await _wiki.ListByDimensionAsync(d, ct);
            all.AddRange(entries);
        }
        return Ok(all);
    }

    /// <summary>
    /// Executes the get entry operation.
    /// </summary>
    /// <param name="entryId">The entry id.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    [HttpGet("entries/{entryId}")]
    public async Task<IActionResult> GetEntry(string entryId, CancellationToken ct)
    {
        var entry = await _wiki.GetAsync(entryId, ct);
        if (entry is null) return NotFound();
        return Ok(entry);
    }

    /// <summary>
    /// Runs the one-shot migration from legacy data/wiki/llm content.
    /// </summary>
    [HttpPost("migrate")]
    public async Task<IActionResult> Migrate(CancellationToken ct)
    {
        var result = await _migration.MigrateAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Runs a one-shot import from OpenClaw wiki/session data hosted on a remote server.
    /// </summary>
    [HttpPost("import/openclaw")]
    public async Task<IActionResult> ImportOpenClaw(
        [FromQuery] bool dryRun = true,
        [FromQuery] bool skipRemoteSync = false,
        [FromQuery] WikiExtractionStrategy strategy = WikiExtractionStrategy.Deterministic,
        [FromQuery] string? runId = null,
        CancellationToken ct = default)
    {
        var result = await _importService.ImportOpenClawAsync(
            new OpenClawImportRequest(dryRun, skipRemoteSync, strategy, null, runId),
            ct);
        return Ok(result);
    }

    /// <summary>
    /// Fixes invalid wiki references (e.g., ../domain/page.md) by using LLM to suggest corrections.
    /// </summary>
    [HttpPost("fix-references")]
    public async Task<IActionResult> FixReferences(
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default)
    {
        var fixer = HttpContext.RequestServices.GetRequiredService<WikiReferenceFixerService>();
        var result = await fixer.FixInvalidReferencesAsync(dryRun, ct);
        return Ok(result);
    }
}

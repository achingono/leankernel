using Microsoft.AspNetCore.Mvc;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Host.Controllers;

[ApiController]
[Route("api/wiki")]
public sealed class WikiController : ControllerBase
{
    private readonly IWikiStore _wiki;

    public WikiController(IWikiStore wiki)
    {
        _wiki = wiki;
    }

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

    [HttpGet("entries/{entryId}")]
    public async Task<IActionResult> GetEntry(string entryId, CancellationToken ct)
    {
        var entry = await _wiki.GetAsync(entryId, ct);
        if (entry is null) return NotFound();
        return Ok(entry);
    }
}

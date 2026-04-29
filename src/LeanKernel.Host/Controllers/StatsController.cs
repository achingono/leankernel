using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Host.Controllers;

[ApiController]
[Route("api/stats")]
public sealed class StatsController : ControllerBase
{
    private static readonly DateTime StartTime = DateTime.UtcNow;
    private readonly ISessionStore _sessions;
    private readonly IWikiStore _wiki;
    private readonly IOptions<LeanKernelConfig> _config;

    public StatsController(
        ISessionStore sessions,
        IWikiStore wiki,
        IOptions<LeanKernelConfig> config)
    {
        _sessions = sessions;
        _wiki = wiki;
        _config = config;
    }

    [HttpGet]
    public async Task<IActionResult> GetStats(CancellationToken ct)
    {
        var sessionList = await _sessions.ListSessionsAsync(ct);

        var wikiCounts = new Dictionary<string, int>();
        var totalFacts = 0;
        foreach (var dim in Enum.GetValues<WikiDimension>())
        {
            var entries = await _wiki.ListByDimensionAsync(dim, ct);
            wikiCounts[dim.ToString().ToLowerInvariant()] = entries.Count;
            totalFacts += entries.Sum(e => e.Facts.Count);
        }

        return Ok(new
        {
            uptime = (DateTime.UtcNow - StartTime).ToString(@"dd\.hh\:mm\:ss"),
            uptimeSeconds = (DateTime.UtcNow - StartTime).TotalSeconds,
            sessions = new
            {
                total = sessionList.Count
            },
            wiki = new
            {
                totalEntries = wikiCounts.Values.Sum(),
                totalFacts,
                dimensions = wikiCounts
            },
            config = new
            {
                model = _config.Value.LiteLlm.DefaultModel,
                contextWindow = _config.Value.LiteLlm.ContextWindowTokens,
                schedulerEnabled = _config.Value.Scheduler.Enabled,
                signalEnabled = _config.Value.Signal.Enabled
            },
            environment = new
            {
                dotnetVersion = Environment.Version.ToString(),
                os = Environment.OSVersion.ToString(),
                processMemoryMb = Math.Round(
                    Environment.WorkingSet / 1_048_576.0, 1)
            }
        });
    }
}

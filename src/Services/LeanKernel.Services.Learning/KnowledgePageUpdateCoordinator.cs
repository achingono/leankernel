using LeanKernel.Logic.Memory;

namespace LeanKernel.Services.Learning;

/// <summary>
/// Coordinates updates to knowledge pages with extracted facts.
/// </summary>
public sealed class KnowledgePageUpdateCoordinator
{
    private readonly IMemoryService _memoryService;
    private readonly ILogger<KnowledgePageUpdateCoordinator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KnowledgePageUpdateCoordinator"/> class.
    /// </summary>
    /// <param name="memoryService">The memory service for persisting knowledge pages.</param>
    /// <param name="logger">The logger.</param>
    public KnowledgePageUpdateCoordinator(
        IMemoryService memoryService,
        ILogger<KnowledgePageUpdateCoordinator> logger)
    {
        _memoryService = memoryService;
        _logger = logger;
    }

    /// <summary>
    /// Writes extracted facts to a knowledge page in memory.
    /// </summary>
    /// <param name="scopeKey">The scope key for the knowledge page.</param>
    /// <param name="facts">The facts to write.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task WriteFactsAsync(string scopeKey, IReadOnlyList<string> facts, CancellationToken ct = default)
    {
        if (facts.Count == 0)
        {
            return;
        }

        var key = $"learning/facts/{scopeKey}/{DateTime.UtcNow:yyyy-MM-dd}";
        var content = string.Join("\n", facts.Select((f, i) => $"{i + 1}. {f}"));

        await _memoryService.PutPageAsync(key, content, ct);
        _logger.LogDebug("Wrote {Count} facts to memory key {Key}", facts.Count, key);
    }
}
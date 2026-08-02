using LeanKernel.Logic.Memory;

namespace LeanKernel.Services.Learning;

/// <summary>
/// Coordinates updates to knowledge pages with extracted facts.
/// Performs read-modify-write to avoid overwriting facts from other turns landing on the same day.
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
    /// Writes extracted facts to a knowledge page in memory using read-modify-write
    /// to preserve facts from earlier turns on the same day.
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
        var existingContent = await TryReadExistingContentAsync(key, ct);
        var mergedContent = MergeFacts(existingContent, facts);

        await _memoryService.PutPageAsync(key, mergedContent, ct);
        _logger.LogDebug("Wrote {Count} facts to memory key {Key}", facts.Count, key);
    }

    private async Task<string?> TryReadExistingContentAsync(string key, CancellationToken ct)
    {
        try
        {
            var page = await _memoryService.GetPageAsync(key, ct);
            return page?.Content;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read existing knowledge page {Key}", key);
            return null;
        }
    }

    private static string MergeFacts(string? existingContent, IReadOnlyList<string> newFacts)
    {
        if (string.IsNullOrWhiteSpace(existingContent))
        {
            return string.Join("\n", newFacts.Select((f, i) => $"{i + 1}. {f}"));
        }

        var existingLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in existingContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 3)
            {
                existingLines.Add(StripNumberPrefix(trimmed));
            }
        }

        var mergedFacts = new List<string>();
        foreach (var line in existingContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            mergedFacts.Add(line.Trim());
        }

        var nextIndex = mergedFacts.Count + 1;
        foreach (var fact in newFacts)
        {
            if (!existingLines.Contains(fact))
            {
                mergedFacts.Add($"{nextIndex}. {fact}");
                nextIndex++;
            }
        }

        return string.Join("\n", mergedFacts);
    }

    private static string StripNumberPrefix(string line)
    {
        var dotIndex = line.IndexOf('.');
        if (dotIndex > 0 && dotIndex < 10)
        {
            var prefix = line[..dotIndex];
            if (int.TryParse(prefix, out _))
            {
                return line[(dotIndex + 1)..].TrimStart();
            }
        }

        return line;
    }
}
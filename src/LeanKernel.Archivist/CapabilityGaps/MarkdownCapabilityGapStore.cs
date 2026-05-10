using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.CapabilityGaps;

/// <summary>
/// Stores capability gaps in a markdown file under the active agent directory.
/// </summary>
public sealed class MarkdownCapabilityGapStore : ICapabilityGapStore
{
    private const int PromptGapLimit = 10;
    private readonly string _path;
    private readonly ILogger<MarkdownCapabilityGapStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MarkdownCapabilityGapStore" /> class.
    /// </summary>
    /// <param name="config">The LeanKernel configuration containing the agent path.</param>
    /// <param name="logger">The logger used for storage diagnostics.</param>
    public MarkdownCapabilityGapStore(
        IOptions<LeanKernelConfig> config,
        ILogger<MarkdownCapabilityGapStore> logger)
    {
        _path = Path.Combine(config.Value.Agents.BasePath, "main", "capability-gaps.md");
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AppendAsync(CapabilityGap gap, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

        if (!File.Exists(_path))
        {
            await File.WriteAllTextAsync(
                _path,
                "# Capability Gaps\n\nObserved gaps that should inform future tool, skill, and behavior improvements.\n\n",
                ct);
        }

        var entry = $"""
            ## {gap.ObservedAt:yyyy-MM-dd HH:mm:ss zzz} - {gap.GapType}

            - Turn event: `{gap.TurnEventId}`
            - Session: `{gap.SessionId}`
            - Request: {Sanitize(gap.UserRequest)}
            - Gap: {Sanitize(gap.Description)}

            """;

        await File.AppendAllTextAsync(_path, entry, ct);
        _logger.LogInformation("Persisted capability gap {GapType} for turn event {TurnEventId}",
            gap.GapType, gap.TurnEventId);
    }

    /// <inheritdoc />
    public async Task<string?> ReadPromptSectionAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return null;

        var lines = await File.ReadAllLinesAsync(_path, ct);
        var gapLines = lines
            .Where(line => line.StartsWith("- Gap:", StringComparison.Ordinal))
            .Select(line => line["- Gap:".Length..].Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(PromptGapLimit)
            .ToList();

        if (gapLines.Count == 0)
            return null;

        return "## Known Capability Gaps\n" +
               "Use these observations to avoid overclaiming and to suggest durable improvements when relevant.\n" +
               string.Join('\n', gapLines.Select(gap => $"- {gap}"));
    }

    private static string Sanitize(string value) =>
        value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
}

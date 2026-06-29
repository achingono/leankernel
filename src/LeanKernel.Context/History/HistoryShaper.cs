using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Persistence;
using LeanKernel.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Context.History;

/// <summary>
/// Provides functionality for history shaper.
/// </summary>
public sealed class HistoryShaper
{
    private readonly HistoryCompactionStrategy _strategy;
    private readonly IConversationCompactor _compactor;
    private readonly ITokenEstimator _tokenEstimator;
    private readonly HistoryConfig _config;
    private readonly IDbContextFactory<LeanKernelDbContext>? _dbContextFactory;
    private readonly ILogger<HistoryShaper> _logger;

    public HistoryShaper(
        HistoryCompactionStrategy strategy,
        IConversationCompactor compactor,
        ITokenEstimator tokenEstimator,
        IOptions<HistoryConfig> config,
        ILogger<HistoryShaper> logger,
        IDbContextFactory<LeanKernelDbContext>? dbContextFactory = null)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _compactor = compactor ?? throw new ArgumentNullException(nameof(compactor));
        _tokenEstimator = tokenEstimator ?? throw new ArgumentNullException(nameof(tokenEstimator));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dbContextFactory = dbContextFactory;
    }

    public async Task<HistoryShapingResult> ShapeAsync(
        string sessionId,
        IReadOnlyList<ConversationTurn> turns,
        int budgetTokens,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(turns);

        if (turns.Count == 0 || budgetTokens <= 0)
        {
            return new HistoryShapingResult
            {
                Diagnostics = new HistoryShapingDiagnostics
                {
                    TotalTurns = turns.Count,
                    DroppedTurns = turns.Count,
                    TotalTokensBefore = turns.Sum(turn => _tokenEstimator.EstimateTokens(turn.Content)),
                    BudgetAvailable = budgetTokens,
                }
            };
        }

        var plan = _strategy.CreatePlan(turns, budgetTokens);
        var segments = new List<HistorySegment>();
        var markers = new List<CompactionMarkerPersistenceRecord>();

        if (plan.SummarizedTurns.Count > 0)
        {
            var summarized = await CreateSegmentAsync(plan.SummarizedTurns, HistoryTier.Summarized, ct).ConfigureAwait(false);
            segments.Add(summarized);
            markers.Add(new CompactionMarkerPersistenceRecord(summarized.Entry.CompactionInfo!, summarized.Entry.Content));
        }

        if (plan.CompactedTurns.Count > 0)
        {
            var compacted = await CreateSegmentAsync(plan.CompactedTurns, HistoryTier.Compacted, ct).ConfigureAwait(false);
            segments.Add(compacted);
            markers.Add(new CompactionMarkerPersistenceRecord(compacted.Entry.CompactionInfo!, compacted.Entry.Content));
        }

        segments.AddRange(plan.VerbatimTurns.Select(CreateVerbatimSegment));

        var totalTokens = segments.Sum(segment => segment.Entry.TokenCount);

        while (segments.Count > 0 && totalTokens > budgetTokens)
        {
            totalTokens -= segments[0].Entry.TokenCount;
            segments.RemoveAt(0);
        }

        var entries = segments.Select(segment => segment.Entry).ToList();
        var history = segments.Select(segment => segment.Turn).ToList();
        var markersToPersist = markers.Select(record => record.Marker).ToList();

        if (_config.PersistCompactionMarkers && markers.Count > 0)
        {
            await PersistMarkersAsync(sessionId, markers, ct).ConfigureAwait(false);
        }

        var verbatimTurns = segments.Where(segment => segment.Entry.Tier == HistoryTier.Verbatim).Sum(segment => segment.SourceTurnCount);
        var compactedTurns = segments.Where(segment => segment.Entry.Tier == HistoryTier.Compacted).Sum(segment => segment.SourceTurnCount);
        var summarizedTurns = segments.Where(segment => segment.Entry.Tier == HistoryTier.Summarized).Sum(segment => segment.SourceTurnCount);

        var diagnostics = new HistoryShapingDiagnostics
        {
            TotalTurns = plan.Diagnostics.TotalTurns,
            VerbatimTurns = verbatimTurns,
            CompactedTurns = compactedTurns,
            SummarizedTurns = summarizedTurns,
            DroppedTurns = plan.Diagnostics.TotalTurns - verbatimTurns - compactedTurns - summarizedTurns,
            TotalTokensBefore = plan.Diagnostics.TotalTokensBefore,
            TotalTokensAfter = totalTokens,
            BudgetAvailable = budgetTokens,
            Markers = markersToPersist,
        };

        _logger.LogDebug(
            "History shaped for session {SessionId}: {Verbatim} verbatim, {Compacted} compacted, {Summarized} summarized, {Dropped} dropped, {Tokens}/{Budget} tokens used",
            sessionId,
            diagnostics.VerbatimTurns,
            diagnostics.CompactedTurns,
            diagnostics.SummarizedTurns,
            diagnostics.DroppedTurns,
            diagnostics.TotalTokensAfter,
            diagnostics.BudgetAvailable);

        return new HistoryShapingResult
        {
            Entries = entries,
            History = history,
            Diagnostics = diagnostics,
        };
    }

    private HistorySegment CreateVerbatimSegment(ConversationTurn turn)
    {
        var tokenCount = _tokenEstimator.EstimateTokens(turn.Content);
        var entry = new ShapedHistoryEntry
        {
            Content = turn.Content,
            Role = turn.Role,
            Tier = HistoryTier.Verbatim,
            OriginalTimestamp = turn.Timestamp,
            OriginalTurnId = turn.TurnId,
            TokenCount = tokenCount,
        };

        return new HistorySegment(entry, turn, 1);
    }

    private async Task<HistorySegment> CreateSegmentAsync(IReadOnlyList<ConversationTurn> turns, HistoryTier tier, CancellationToken ct)
    {
        var content = tier == HistoryTier.Compacted
            ? await _compactor.CompactAsync(turns, ct).ConfigureAwait(false)
            : await _compactor.SummarizeAsync(turns, ct).ConfigureAwait(false);
        var tokenCount = _tokenEstimator.EstimateTokens(content);
        var originalTokenCount = turns.Sum(turn => _tokenEstimator.EstimateTokens(turn.Content));
        var markerType = tier == HistoryTier.Compacted ? "compacted" : "summarized";
        var marker = new CompactionMarker
        {
            MarkerType = markerType,
            CompactedAt = DateTimeOffset.UtcNow,
            OriginalTurnCount = turns.Count,
            OriginalTokenCount = originalTokenCount,
            CompactedTokenCount = tokenCount,
            CompactedBy = _config.CompactionModel,
        };
        var sourceId = BuildSourceId(turns);
        var timestamp = turns.Last().Timestamp;
        var role = "assistant";
        var entry = new ShapedHistoryEntry
        {
            Content = content,
            Role = role,
            Tier = tier,
            OriginalTimestamp = timestamp,
            TokenCount = tokenCount,
            CompactionInfo = marker,
        };
        var turn = new ConversationTurn
        {
            Role = role,
            Content = content,
            Timestamp = timestamp,
            IsCompacted = true,
            CompactionSourceId = sourceId,
        };

        return new HistorySegment(entry, turn, turns.Count);
    }

    private async Task PersistMarkersAsync(
        string sessionId,
        IReadOnlyList<CompactionMarkerPersistenceRecord> markers,
        CancellationToken ct)
    {
        if (_dbContextFactory is null)
        {
            _logger.LogDebug("Skipping compaction marker persistence for session {SessionId} because no DbContext factory is registered", sessionId);
            return;
        }

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        db.CompactionMarkers.AddRange(markers.Select(record => new CompactionMarkerEntity
        {
            SessionId = sessionId,
            MarkerType = record.Marker.MarkerType,
            CompactedAt = record.Marker.CompactedAt,
            OriginalTurnCount = record.Marker.OriginalTurnCount,
            OriginalTokenCount = record.Marker.OriginalTokenCount,
            CompactedTokenCount = record.Marker.CompactedTokenCount,
            CompactedContent = record.Content,
            CompactedBy = record.Marker.CompactedBy,
        }));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string? BuildSourceId(IReadOnlyList<ConversationTurn> turns)
    {
        var first = turns.FirstOrDefault(turn => !string.IsNullOrWhiteSpace(turn.TurnId))?.TurnId;
        var last = turns.LastOrDefault(turn => !string.IsNullOrWhiteSpace(turn.TurnId))?.TurnId;

        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
        {
            return null;
        }

        return string.Equals(first, last, StringComparison.Ordinal)
            ? first
            : $"{first}..{last}";
    }

    private sealed record HistorySegment(ShapedHistoryEntry Entry, ConversationTurn Turn, int SourceTurnCount);

    private sealed record CompactionMarkerPersistenceRecord(CompactionMarker Marker, string Content);
}

/// <summary>
/// Provides functionality for history shaping result.
/// </summary>
public sealed record HistoryShapingResult
{
    /// <summary>
    /// Gets or sets history.
    /// </summary>
    public IReadOnlyList<ConversationTurn> History { get; init; } = [];
    /// <summary>
    /// Gets or sets entries.
    /// </summary>
    public IReadOnlyList<ShapedHistoryEntry> Entries { get; init; } = [];
    public HistoryShapingDiagnostics Diagnostics { get; init; } = new();
}

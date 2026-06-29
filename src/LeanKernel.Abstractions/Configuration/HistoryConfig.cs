namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configuration settings for history management.
/// </summary>
public sealed class HistoryConfig
{
    /// <summary>
    /// Gets or sets the number of recent turns to keep verbatim.
    /// </summary>
    public int RecentTurnsVerbatim { get; set; } = 6;

    /// <summary>
    /// Gets or sets the maximum number of compacted turns.
    /// </summary>
    public int CompactedTurnsMax { get; set; } = 10;

    /// <summary>
    /// Gets or sets the maximum number of summarized turns.
    /// </summary>
    public int SummarizedTurnsMax { get; set; } = 20;

    /// <summary>
    /// Gets or sets whether compaction is enabled.
    /// </summary>
    public bool EnableCompaction { get; set; } = true;

    /// <summary>
    /// Gets or sets whether summarization is enabled.
    /// </summary>
    public bool EnableSummarization { get; set; } = true;

    /// <summary>
    /// Gets or sets the model used for compaction.
    /// </summary>
    public string CompactionModel { get; set; } = "small";

    /// <summary>
    /// Gets or sets the temperature used for compaction.
    /// </summary>
    public double CompactionTemperature { get; set; } = 0.1;

    /// <summary>
    /// Gets or sets the maximum tokens for summarization.
    /// </summary>
    public int MaxSummaryTokens { get; set; } = 200;

    /// <summary>
    /// Gets or sets whether to persist compaction markers.
    /// </summary>
    public bool PersistCompactionMarkers { get; set; } = true;
}

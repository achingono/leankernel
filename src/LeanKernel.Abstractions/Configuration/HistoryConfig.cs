namespace LeanKernel.Abstractions.Configuration;

public sealed class HistoryConfig
{
    public int RecentTurnsVerbatim { get; set; } = 6;
    public int CompactedTurnsMax { get; set; } = 10;
    public int SummarizedTurnsMax { get; set; } = 20;
    public bool EnableCompaction { get; set; } = true;
    public bool EnableSummarization { get; set; } = true;
    public string CompactionModel { get; set; } = "gpt-4o-mini";
    public double CompactionTemperature { get; set; } = 0.1;
    public int MaxSummaryTokens { get; set; } = 200;
    public bool PersistCompactionMarkers { get; set; } = true;
}

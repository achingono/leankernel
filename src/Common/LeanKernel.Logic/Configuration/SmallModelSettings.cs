namespace LeanKernel.Logic.Configuration;

public sealed class SmallModelSettings
{
    public const string SectionName = "LeanKernel:SmallModel";

    public string ModelId { get; set; } = "gpt-4o-mini";
    public int MaxOutputTokens { get; set; } = 512;
    public int MaxConcurrency { get; set; } = 4;
    public int TimeoutSeconds { get; set; } = 15;
    public bool Enabled { get; set; } = true;
}

namespace LeanKernel.Logic.Configuration;

public sealed class FactExtractionSettings
{
    public const string SectionName = "LeanKernel:FactExtraction";

    public string ModelId { get; set; } = "gpt-4o-mini";
    public double Temperature { get; set; } = 0.1;
    public int MaxOutputTokens { get; set; } = 1024;
}

namespace LeanKernel.Core.Configuration;

/// <summary>
/// Root configuration model, bound from appsettings.json under "LeanKernel" key.
/// </summary>
public sealed class LeanKernelConfig
{
    public const string SectionName = "LeanKernel";

    public LiteLlmConfig LiteLlm { get; set; } = new();
    public QdrantConfig Qdrant { get; set; } = new();
    public SignalConfig Signal { get; set; } = new();
    public WikiConfig Wiki { get; set; } = new();
    public ContextConfig Context { get; set; } = new();
    public SchedulerConfig Scheduler { get; set; } = new();
}

public sealed class LiteLlmConfig
{
    public string BaseUrl { get; set; } = "http://litellm:4000";
    public string ApiKey { get; set; } = "sk-LeanKernel-local";
    public string DefaultModel { get; set; } = "gpt-4o-mini";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public int ContextWindowTokens { get; set; } = 128_000;
}

public sealed class QdrantConfig
{
    public string Host { get; set; } = "qdrant";
    public int Port { get; set; } = 6334;
    public string CollectionName { get; set; } = "LEANKERNEL_wiki";
    public int EmbeddingDimension { get; set; } = 1536;
}

public sealed class SignalConfig
{
    public string CliPath { get; set; } = "/usr/local/bin/signal-cli";
    public string Account { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class WikiConfig
{
    public string BasePath { get; set; } = "/app/data/wiki";
    public int MaxFactsPerEntry { get; set; } = 20;
    public int StaleFactDays { get; set; } = 30;
    public double MinConfidenceThreshold { get; set; } = 0.5;
}

public sealed class ContextConfig
{
    public double SemanticSimilarityWeight { get; set; } = 0.40;
    public double RecencyDecayWeight { get; set; } = 0.20;
    public double DimensionMatchWeight { get; set; } = 0.25;
    public double InteractionFrequencyWeight { get; set; } = 0.15;
    public double MinRelevanceThreshold { get; set; } = 0.65;
    public int MaxConversationTurns { get; set; } = 15;
}

public sealed class SchedulerConfig
{
    public bool Enabled { get; set; } = true;
    public string WikiMaintenanceCron { get; set; } = "0 3 * * *"; // 3 AM daily
}

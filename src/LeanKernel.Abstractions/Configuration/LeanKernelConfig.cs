namespace LeanKernel.Abstractions.Configuration;

public sealed class LeanKernelConfig
{
    public const string SectionName = "LeanKernel";

    public LiteLlmConfig LiteLlm { get; set; } = new();
    public ContextConfig Context { get; set; } = new();
    public RetrievalConfig Retrieval { get; set; } = new();
    public HistoryConfig History { get; set; } = new();
    public RoutingConfig Routing { get; set; } = new();
    public OrchestrationConfig Orchestration { get; set; } = new();
    public GBrainConfig GBrain { get; set; } = new();
    public IdentityConfig Identity { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public DiagnosticsConfig Diagnostics { get; set; } = new();
    public ChannelsConfig Channels { get; set; } = new();
    public EnhancementConfig Enhancement { get; set; } = new();
    public LearningConfig Learning { get; set; } = new();
    public FileSystemConfig FileSystem { get; set; } = new();

    /// <summary>
    /// Gets or sets production-hardening configuration.
    /// </summary>
    public HardeningConfig Hardening { get; set; } = new();

    /// <summary>
    /// Gets or sets the scheduler configuration.
    /// </summary>
    public SchedulerConfig Scheduler { get; set; } = new();
}

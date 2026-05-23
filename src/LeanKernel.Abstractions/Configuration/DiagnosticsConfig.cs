namespace LeanKernel.Abstractions.Configuration;

public sealed class DiagnosticsConfig
{
    public bool Enabled { get; set; } = true;
    public bool PersistToDatabase { get; set; } = true;
    public bool ContextDiagnosticsEnabled { get; set; } = true;
    public int MaxDiagnosticsPerSession { get; set; } = 100;
    public string ServiceName { get; set; } = "leankernel";
}

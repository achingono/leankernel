namespace LeanKernel.Gateway.Configuration;

public class AgentSettings
{
    public string RootPath { get; set; } = "agents";
    public string DefaultName { get; set; } = "LeanKernel";
    public string DefaultInstructions { get; set; } = "You are a helpful AI assistant.";
    public string DefaultDescription { get; set; } = string.Empty;
}
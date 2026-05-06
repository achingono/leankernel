using Microsoft.Extensions.Logging;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Agents;

/// <summary>
/// Base worker agent. Stateless, single-purpose, lean.
/// Each worker operates with a constrained context budget and
/// a specialized system prompt for its domain.
/// Uses MAF ChatClientAgent via AgentFactory for LLM calls.
/// Workers can be exposed as AIFunction tools via Agent-as-Tool pattern.
/// </summary>
public class WorkerAgent
{
    public AgentDefinition Definition { get; }
    public AgentFactory AgentFactory { get; }
    protected readonly ILogger Logger;

    public WorkerAgent(
        AgentDefinition definition,
        AgentFactory agentFactory,
        ILogger logger)
    {
        Definition = definition;
        AgentFactory = agentFactory;
        Logger = logger;
    }

    public virtual async Task<string> ExecuteAsync(
        string task,
        ContextBudget budget,
        CancellationToken ct)
    {
        Logger.LogDebug("Worker [{Name}] executing: {Task}", Definition.Name,
            task.Length > 100 ? task[..100] + "..." : task);

        try
        {
            var agent = AgentFactory.CreateAgent(Definition.SystemPrompt);
            var response = await agent.RunAsync(task, cancellationToken: ct);
            return response.Text ?? "No response generated.";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Worker [{Name}] failed", Definition.Name);
            return $"Worker error: {ex.Message}";
        }
    }
}

/// <summary>
/// Research worker: web search + summarize results.
/// </summary>
public sealed class ResearchWorker : WorkerAgent
{
    public ResearchWorker(AgentFactory agentFactory, ILogger<ResearchWorker> logger)
        : base(
            new AgentDefinition
            {
                Name = "research",
                Description = "Research and summarize information from the web",
                SystemPrompt = "You are a research assistant. Search for information and provide concise, factual summaries. Always cite your sources.",
                MaxContextTokens = 4_000,
                AllowedTools = ["screenshot_ocr", "doughray", "simplefin"],
                AllowedCategories = ["research", "information", "search"],
                Categories = ["research", "information", "search"]
            },
            agentFactory,
            logger)
    { }
}

/// <summary>
/// Code worker: code generation with focused context.
/// </summary>
public sealed class CodeWorker : WorkerAgent
{
    public CodeWorker(AgentFactory agentFactory, ILogger<CodeWorker> logger)
        : base(
            new AgentDefinition
            {
                Name = "code",
                Description = "Generate, review, and explain code",
                SystemPrompt = "You are a coding assistant. Write clean, efficient code. Explain your approach briefly. Focus on correctness and readability.",
                MaxContextTokens = 8_000,
                AllowedTools = ["screenshot_ocr"],
                AllowedCategories = ["code", "programming", "development"],
                Categories = ["code", "programming", "development"]
            },
            agentFactory,
            logger)
    { }
}

/// <summary>
/// Schedule worker: calendar and reminder management.
/// </summary>
public sealed class ScheduleWorker : WorkerAgent
{
    public ScheduleWorker(AgentFactory agentFactory, ILogger<ScheduleWorker> logger)
        : base(
            new AgentDefinition
            {
                Name = "schedule",
                Description = "Manage schedules, reminders, and calendar events",
                SystemPrompt = "You are a scheduling assistant. Help organize time, set reminders, and manage calendar events. Be precise with dates and times.",
                MaxContextTokens = 2_000,
                AllowedTools = ["mstodo", "emanate"],
                AllowedCategories = ["schedule", "calendar", "reminder"],
                Categories = ["schedule", "calendar", "reminder"]
            },
            agentFactory,
            logger)
    { }
}

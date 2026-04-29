using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.SemanticKernel;

namespace LeanKernel.Thinker.Agents;

/// <summary>
/// Base worker agent. Stateless, single-purpose, lean.
/// Each worker operates with a constrained context budget and
/// a specialized system prompt for its domain.
/// </summary>
public class WorkerAgent
{
    public AgentDefinition Definition { get; }
    protected readonly KernelFactory KernelFactory;
    protected readonly ILogger Logger;

    public WorkerAgent(
        AgentDefinition definition,
        KernelFactory kernelFactory,
        ILogger logger)
    {
        Definition = definition;
        KernelFactory = kernelFactory;
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
            var kernel = KernelFactory.Build();
            var chatService = kernel.GetRequiredService<IChatCompletionService>();

            var history = new ChatHistory();
            history.AddSystemMessage(Definition.SystemPrompt);
            history.AddUserMessage(task);

            var response = await chatService.GetChatMessageContentAsync(
                history, cancellationToken: ct);

            return response.Content ?? "No response generated.";
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
    public ResearchWorker(KernelFactory kernelFactory, ILogger<ResearchWorker> logger)
        : base(
            new AgentDefinition
            {
                Name = "research",
                Description = "Research and summarize information from the web",
                SystemPrompt = "You are a research assistant. Search for information and provide concise, factual summaries. Always cite your sources.",
                MaxContextTokens = 4_000,
                Categories = ["research", "information", "search"]
            },
            kernelFactory,
            logger)
    { }
}

/// <summary>
/// Code worker: code generation with focused context.
/// </summary>
public sealed class CodeWorker : WorkerAgent
{
    public CodeWorker(KernelFactory kernelFactory, ILogger<CodeWorker> logger)
        : base(
            new AgentDefinition
            {
                Name = "code",
                Description = "Generate, review, and explain code",
                SystemPrompt = "You are a coding assistant. Write clean, efficient code. Explain your approach briefly. Focus on correctness and readability.",
                MaxContextTokens = 8_000,
                Categories = ["code", "programming", "development"]
            },
            kernelFactory,
            logger)
    { }
}

/// <summary>
/// Schedule worker: calendar and reminder management.
/// </summary>
public sealed class ScheduleWorker : WorkerAgent
{
    public ScheduleWorker(KernelFactory kernelFactory, ILogger<ScheduleWorker> logger)
        : base(
            new AgentDefinition
            {
                Name = "schedule",
                Description = "Manage schedules, reminders, and calendar events",
                SystemPrompt = "You are a scheduling assistant. Help organize time, set reminders, and manage calendar events. Be precise with dates and times.",
                MaxContextTokens = 2_000,
                Categories = ["schedule", "calendar", "reminder"]
            },
            kernelFactory,
            logger)
    { }
}

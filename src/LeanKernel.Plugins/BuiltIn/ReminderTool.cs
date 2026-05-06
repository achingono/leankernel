using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Creates cron-based reminders. Schedules a message to be sent
/// at a future time via the scheduler.
/// </summary>
[ToolMetadata(
    Name = "reminder",
    Description = "Create a reminder that triggers at a specified time.",
    Category = ToolCategory.Scheduling)]
public sealed class ReminderTool : ITool
{
    private readonly IScheduler _scheduler;

    public string Name => "reminder";
    public string Description => "Create a scheduled reminder.";
    public string Category => ToolCategory.Scheduling.ToString().ToLower();
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "message": { "type": "string", "description": "Reminder message" },
            "cron": { "type": "string", "description": "Cron expression for when to trigger" },
            "id": { "type": "string", "description": "Unique reminder ID" }
          },
          "required": ["message", "cron", "id"]
        }
        """;

    public ReminderTool(IScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var doc = JsonDocument.Parse(parametersJson);
            var message = doc.RootElement.GetProperty("message").GetString()!;
            var cron = doc.RootElement.GetProperty("cron").GetString()!;
            var id = doc.RootElement.GetProperty("id").GetString()!;

            var jobId = $"reminder-{id}";

            await _scheduler.ScheduleAsync(jobId, cron, async _ =>
            {
                // In a full implementation, this would send via a channel
                // For now, log the reminder
                Console.WriteLine($"[REMINDER] {message}");
            }, ct);

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = $"Reminder '{id}' scheduled: {message} (cron: {cron})",
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = ex.Message,
                Duration = sw.Elapsed
            };
        }
    }
}

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

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name => "reminder";
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description => "Create a scheduled reminder.";
    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public string Category => ToolCategory.Scheduling.ToString().ToLower();
    /// <summary>
    /// Gets or sets the parameters schema.
    /// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ReminderTool" /> class.
    /// </summary>
    /// <param name="scheduler">The scheduler.</param>
    public ReminderTool(IScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    /// <summary>
    /// Executes the execute async operation.
    /// </summary>
    /// <param name="parametersJson">The parameters json.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
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
                // Reminder delivery is intentionally isolated from channel routing.
                // The scheduled callback records the reminder payload for the host to observe.
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

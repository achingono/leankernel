using System.Diagnostics;
using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn.OpenclaSkills;

/// <summary>
/// Microsoft To Do CLI wrapper — manage lists, tasks, and reminders
/// in Microsoft To Do.
/// </summary>
[ToolMetadata(
    Name = "mstodo_skill",
    Description = "Microsoft To Do CLI: manage lists, tasks, attachments, and checklist steps. Use for capturing follow-ups, creating reminders, reviewing task lists, and completing actionable items.",
    Category = ToolCategory.General)]
public sealed class MsTodoSkillTool : ITool
{
    public string Name => "mstodo_skill";
    public string Description => "Manage Microsoft To Do via CLI.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "operation": { 
              "type": "string", 
              "description": "Operation: list_groups, list_lists, create_list, create_task, list_tasks, complete_task, search_tasks, delete_task, get_task",
              "enum": ["list_groups", "list_lists", "create_list", "create_task", "list_tasks", "complete_task", "search_tasks", "delete_task", "get_task"]
            },
            "list_name": { "type": "string", "description": "Name of the list" },
            "list_id": { "type": "string", "description": "List ID" },
            "task_title": { "type": "string", "description": "Title for new task" },
            "task_body": { "type": "string", "description": "Task body/description" },
            "task_id": { "type": "string", "description": "Task ID" },
            "due_date": { "type": "string", "description": "Due date (ISO 8601)" },
            "query": { "type": "string", "description": "Search query" }
          },
          "required": ["operation"]
        }
        """;

    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            var root = doc.RootElement;
            var operation = root.GetProperty("operation").GetString() ?? "";

            var args = operation switch
            {
                "list_groups" => "group list",
                "list_lists" => "list list",
                "create_list" => BuildCreateListArgs(root),
                "create_task" => BuildCreateTaskArgs(root),
                "list_tasks" => BuildListTasksArgs(root),
                "complete_task" => BuildCompleteTaskArgs(root),
                "search_tasks" => BuildSearchTasksArgs(root),
                "delete_task" => BuildDeleteTaskArgs(root),
                "get_task" => BuildGetTaskArgs(root),
                _ => throw new ArgumentException($"Unknown operation: {operation}")
            };

            var output = await ExecuteCliCommand("ms-todo-cli", args, ct);

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = output,
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

    private static string BuildCreateListArgs(JsonElement root)
    {
        var listName = root.GetProperty("list_name").GetString();
        return $"list create \"{listName}\"";
    }

    private static string BuildCreateTaskArgs(JsonElement root)
    {
        var listId = root.GetProperty("list_id").GetString();
        var taskTitle = root.GetProperty("task_title").GetString();
        var args = $"task create --list-id {listId} --title \"{taskTitle}\"";

        if (root.TryGetProperty("task_body", out var bodyElem))
            args += $" --body \"{bodyElem.GetString()}\"";
        if (root.TryGetProperty("due_date", out var dueElem))
            args += $" --due-date {dueElem.GetString()}";

        return args;
    }

    private static string BuildListTasksArgs(JsonElement root)
    {
        var args = "task list";
        if (root.TryGetProperty("list_id", out var listElem))
            args += $" --list-id {listElem.GetString()}";
        return args;
    }

    private static string BuildCompleteTaskArgs(JsonElement root)
    {
        var taskId = root.GetProperty("task_id").GetString();
        return $"task complete {taskId}";
    }

    private static string BuildSearchTasksArgs(JsonElement root)
    {
        var query = root.GetProperty("query").GetString();
        return $"task search \"{query}\"";
    }

    private static string BuildDeleteTaskArgs(JsonElement root)
    {
        var taskId = root.GetProperty("task_id").GetString();
        return $"task delete {taskId}";
    }

    private static string BuildGetTaskArgs(JsonElement root)
    {
        var taskId = root.GetProperty("task_id").GetString();
        return $"task get {taskId}";
    }

    private static async Task<string> ExecuteCliCommand(string command, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {command}");
        
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(cts.Token);
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                return $"Command failed with exit code {process.ExitCode}: {error}";
            }

            return await process.StandardOutput.ReadToEndAsync();
        }
        catch (OperationCanceledException)
        {
            process.Kill();
            return "Command execution timed out";
        }
    }
}

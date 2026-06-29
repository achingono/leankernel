using System.Text;
using System.Text.Json;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents.ToolSelection;

/// <summary>
/// Provides functionality for tool selector.
/// </summary>
public sealed class ToolSelector : IToolSelector
{
    private readonly AgentFactory _agentFactory;
    private readonly string _economyModel;
    private readonly ILogger<ToolSelector> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ToolSelector(
        AgentFactory agentFactory,
        IOptions<LeanKernelConfig> config,
        ILogger<ToolSelector> logger)
    {
        ArgumentNullException.ThrowIfNull(agentFactory);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _agentFactory = agentFactory;
        _economyModel = config.Value.Routing.Economy.Model;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ToolDefinition>> SelectToolsAsync(
        string userMessage,
        IReadOnlyList<ToolDefinition> allTools,
        int maxTools,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(userMessage);
        ArgumentNullException.ThrowIfNull(allTools);

        if (allTools.Count <= maxTools)
        {
            return allTools;
        }

        try
        {
            var manifest = BuildToolManifest(allTools);
            var prompt = BuildSelectionPrompt(userMessage, manifest, maxTools);

            var chatClient = _agentFactory.GetChatClientForModel(_economyModel);
            var response = await chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, prompt)
            ], new ChatOptions
            {
                MaxOutputTokens = 512,
                Temperature = 0.1f
            }, ct).ConfigureAwait(false);

            var selectedNames = ParseToolNames(response.Text);

            if (selectedNames.Count == 0)
            {
                _logger.LogWarning(
                    "Tool selection parsed no tool names, falling back to first {MaxTools} tools",
                    maxTools);
                return allTools.Take(maxTools).ToList();
            }

            var selectedTools = allTools
                .Where(t => selectedNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
                .Take(maxTools)
                .ToList();

            if (selectedTools.Count == 0)
            {
                _logger.LogWarning(
                    "Tool selection returned no matching tools, falling back to first {MaxTools} tools",
                    maxTools);
                return allTools.Take(maxTools).ToList();
            }

            _logger.LogInformation(
                "Tool selection: economy model selected {SelectedCount}/{TotalCount} tools (max {MaxTools})",
                selectedTools.Count,
                allTools.Count,
                maxTools);

            return selectedTools;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Tool selection timed out, falling back to first {MaxTools} tools", maxTools);
            return allTools.Take(maxTools).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tool selection failed, falling back to first {MaxTools} tools", maxTools);
            return allTools.Take(maxTools).ToList();
        }
    }

    private static string BuildToolManifest(IReadOnlyList<ToolDefinition> tools)
    {
        var sb = new StringBuilder();
        foreach (var tool in tools)
        {
            sb.AppendLine($"- {tool.Name}: {tool.Description ?? "(no description)"}");
        }

        return sb.ToString();
    }

    private static string BuildSelectionPrompt(string userMessage, string manifest, int maxTools)
    {
        return $"""
You are a tool router. Given a user request and a list of available tools, select the most relevant tools the agent will need to fulfill the request.

Rules:
- Select at most {maxTools} tools.
- Return ONLY a JSON array of tool names. No explanation, no markdown.
- Include general-purpose tools (web search, file access, knowledge lookup) when they might be needed.

User request: {userMessage}

Available tools:
{manifest}

Return a JSON array of tool names from the list above.
""";
    }

    private static IReadOnlyList<string> ParseToolNames(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return [];
        }

        // Strip markdown code fences if present
        var cleaned = response.Trim();
        if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[7..].TrimStart();
        }
        else if (cleaned.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[3..].TrimStart();
        }

        if (cleaned.EndsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            cleaned = cleaned[..^3].TrimEnd();
        }

        try
        {
            var names = JsonSerializer.Deserialize<List<string>>(cleaned, JsonOptions);
            return names is { Count: > 0 }
                ? names
                : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System.Diagnostics;
using System.Text.Json;

namespace LeanKernel.Logic.Tools.ToolSelection;

/// <summary>
/// Uses the economy-tier small model to select the most relevant tools when the count exceeds <c>MaxTools</c>.
/// Falls back to the first N tools in registration order on timeout or parse failure.
/// </summary>
public sealed class ToolSelector : IToolSelector
{
    private readonly IOptions<ToolSettings> _toolSettings;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ToolSelector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolSelector"/> class.
    /// </summary>
    /// <param name="toolSettings">The tool settings.</param>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="logger">The logger.</param>
    public ToolSelector(
        IOptions<ToolSettings> toolSettings,
        IServiceProvider serviceProvider,
        ILogger<ToolSelector> logger)
    {
        _toolSettings = toolSettings;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ToolDefinition>> SelectToolsAsync(
        string userMessage,
        IReadOnlyList<ToolDefinition> allTools,
        int maxTools,
        CancellationToken cancellationToken = default)
    {
        if (allTools.Count <= maxTools)
        {
            return allTools;
        }

        // 90% threshold logging is handled by the caller; here we log the selection attempt
        _logger.LogInformation(
            "ToolSelector: selecting {Max} of {Total} tools for message: {MessageSnippet}",
            maxTools, allTools.Count, Truncate(userMessage, 120));

        try
        {
            var manifest = BuildCompactManifest(allTools);
            var selectedNames = await CallSmallModelAsync(userMessage, manifest, maxTools, cancellationToken).ConfigureAwait(false);

            if (selectedNames is null || selectedNames.Count == 0)
            {
                _logger.LogWarning("ToolSelector: small model returned no names, falling back to first {Max} tools.", maxTools);
                return allTools.Take(maxTools).ToList();
            }

            var selectedSet = new HashSet<string>(selectedNames, StringComparer.Ordinal);
            var filtered = allTools.Where(t => selectedSet.Contains(t.Name)).ToList();

            // If the model returned too few or too many, clamp to maxTools
            if (filtered.Count == 0)
            {
                _logger.LogWarning("ToolSelector: no selected names matched registry, falling back to first {Max}.", maxTools);
                return allTools.Take(maxTools).ToList();
            }

            if (filtered.Count > maxTools)
            {
                filtered = filtered.Take(maxTools).ToList();
            }

            // Ensure we always return maxTools if possible by filling with remaining tools in order
            if (filtered.Count < maxTools)
            {
                var remaining = allTools.Where(t => !selectedSet.Contains(t.Name)).Take(maxTools - filtered.Count);
                filtered.AddRange(remaining);
            }

            _logger.LogInformation(
                "ToolSelector: selected {Selected} of {Total} tools (requested {Max}).",
                filtered.Count, allTools.Count, maxTools);

            return filtered;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ToolSelector: selection failed, falling back to first {Max} tools.", maxTools);
            return allTools.Take(maxTools).ToList();
        }
    }

    private static string BuildCompactManifest(IReadOnlyList<ToolDefinition> tools)
    {
        // Name + 1-line description only, skip JSON schema
        var lines = tools.Select(t => $"- {t.Name}: {FirstLine(t.Description)}");
        return string.Join("\n", lines);
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
        if (line.Length > 160)
        {
            line = line[..157] + "...";
        }

        return line;
    }

    private async Task<IReadOnlyList<string>?> CallSmallModelAsync(
        string userMessage,
        string manifest,
        int maxTools,
        CancellationToken cancellationToken)
    {
        // Resolve the small-model chat client (same as Memory's ReasoningModel)
        // Fall back to the default chat client if small-model is not available
        IChatClient? chatClient = null;
        try
        {
            chatClient = _serviceProvider.GetKeyedService<IChatClient>("small-model") ?? _serviceProvider.GetService<IChatClient>();
        }
        catch
        {
            // ignore
        }

        if (chatClient is null)
        {
            _logger.LogWarning("ToolSelector: no small-model chat client available, using fallback.");
            return null;
        }

        var systemPrompt = $@"You are a tool selector. Given the user message and the available tools (name: description), return a JSON array of the most relevant tool names, up to {maxTools} items. Return ONLY a JSON array of strings, no other text. Prefer tools whose description matches the user's intent. If the intent is ambiguous, include a diverse set covering the likely intents.";

        var userPrompt = $@"User message: {Truncate(userMessage, 500)}

Available tools:
{manifest}

Return JSON array of tool names (max {maxTools}):";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt),
        };

        var options = new ChatOptions
        {
            MaxOutputTokens = 256,
            Temperature = 0.1f,
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var response = await chatClient.GetResponseAsync(messages, options, cts.Token).ConfigureAwait(false);
            var content = response.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            // Extract JSON array if the model added surrounding text
            var jsonStart = content.IndexOf('[');
            var jsonEnd = content.LastIndexOf(']');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                content = content[jsonStart..(jsonEnd + 1)];
            }

            var names = JsonSerializer.Deserialize<string[]>(content, new JsonSerializerOptions { AllowTrailingCommas = true });
            return names;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ToolSelector: small model call timed out after 5s, falling back.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ToolSelector: small model call failed, falling back.");
            return null;
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text ?? string.Empty;
        }

        return text[..maxLength] + "...";
    }
}

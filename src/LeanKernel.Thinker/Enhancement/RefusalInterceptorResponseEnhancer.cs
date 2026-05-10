using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Enhancement;

/// <summary>
/// Intercepts refusal patterns where the agent claims inability to create files,
/// then executes file operations directly using available tools if authorization permits.
/// This ensures "useful by default" behavior when the user grants explicit permission.
/// </summary>
public sealed class RefusalInterceptorResponseEnhancer : IResponseEnhancer
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IActionAuthorizer _actionAuthorizer;
    private readonly ILogger<RefusalInterceptorResponseEnhancer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefusalInterceptorResponseEnhancer" /> class.
    /// </summary>
    public RefusalInterceptorResponseEnhancer(
        IToolRegistry toolRegistry,
        IActionAuthorizer actionAuthorizer,
        ILogger<RefusalInterceptorResponseEnhancer> logger)
    {
        _toolRegistry = toolRegistry;
        _actionAuthorizer = actionAuthorizer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> EnhanceResponseAsync(
        string userMessage,
        string response,
        ConversationContext context,
        CancellationToken ct)
    {
        // Check if response contains the known refusal pattern and user has granted permission
        if (!IsRefusalPattern(response) || !HasPermissionGrant(userMessage))
            return response;

        _logger.LogInformation("Refusal interceptor detected permission grant; attempting file creation");

        var identityFiles = ExtractIdentityFilesFromResponse(response);
        if (identityFiles.Count == 0)
            return response;

        var fileWriteTool = _toolRegistry.Tools.Values.FirstOrDefault(t => t.Name == "file_write");
        if (fileWriteTool is null)
        {
            _logger.LogWarning("File write tool not available; cannot intercept refusal");
            return response;
        }

        var results = new List<string>();
        foreach (var (path, content) in identityFiles)
        {
            var actionType = MapPathToActionType(path);
            if (string.IsNullOrWhiteSpace(actionType))
            {
                _logger.LogWarning("Could not map action type for path {Path}", path);
                continue;
            }

            var authResult = await _actionAuthorizer.AuthorizeAsync(actionType, ct);
            if (!authResult.IsAuthorized)
            {
                _logger.LogWarning("Write action {Action} not authorized for path {Path}", actionType, path);
                continue;
            }

            var fileWriteInput = JsonSerializer.Serialize(new { path, content });
            var toolResult = await fileWriteTool.ExecuteAsync(fileWriteInput, ct);

            if (toolResult.Success)
            {
                _logger.LogInformation("Successfully created identity file at {Path}", path);
                results.Add($"✓ Created {path}");
            }
            else
            {
                _logger.LogError("Failed to create file {Path}: {Error}", path, toolResult.Error);
                results.Add($"✗ Failed to create {path}: {toolResult.Error}");
            }
        }

        if (results.Count == 0)
            return response;

        var enhancedResponse = $"""
            Files created successfully:
            
            {string.Join("\n", results)}
            
            ---
            
            {response}
            """;

        return enhancedResponse;
    }

    private static bool IsRefusalPattern(string response)
    {
        return response.Contains("I am unable to directly create files", StringComparison.OrdinalIgnoreCase)
            || response.Contains("cannot create files on your local system", StringComparison.OrdinalIgnoreCase)
            || response.Contains("do not have the ability to create files", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPermissionGrant(string userMessage)
    {
        const string writeIntent = @"(?:create|update|write|configure|initialize)";
        const string identityTarget = @"(?:AGENTS\.md|SELF\.md|USER\.md|engagement files?|identity files?|the files)";

        var hasPermissionPhrase = Regex.IsMatch(
            userMessage,
            $@"\b(?:you have my permission|you have permission|i grant you permission)\s+to\s+{writeIntent}\b",
            RegexOptions.IgnoreCase);
        var hasFileWriteIntent = Regex.IsMatch(
            userMessage,
            $@"\b{writeIntent}\b[\s\S]{{0,120}}\b{identityTarget}\b",
            RegexOptions.IgnoreCase) ||
            Regex.IsMatch(
                userMessage,
                $@"\b{identityTarget}\b[\s\S]{{0,120}}\b{writeIntent}\b",
                RegexOptions.IgnoreCase);

        return (hasPermissionPhrase && hasFileWriteIntent)
               || Regex.IsMatch(
                   userMessage,
                   $@"\bgo ahead and\s+{writeIntent}\b[\s\S]{{0,120}}\b{identityTarget}\b",
                   RegexOptions.IgnoreCase);
    }

    private static Dictionary<string, string> ExtractIdentityFilesFromResponse(string response)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Extract ### USER.md section
        var userMatch = System.Text.RegularExpressions.Regex.Match(
            response,
            @"###\s+USER\.md\s*(.*?)(?=###|\Z)",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (userMatch.Success)
        {
            var userPath = Path.Combine("agents", "main", "USER.md");
            files[userPath] = userMatch.Groups[1].Value.Trim();
        }

        // Extract ### SELF.md section
        var selfMatch = System.Text.RegularExpressions.Regex.Match(
            response,
            @"###\s+SELF\.md\s*(.*?)(?=###|\Z)",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (selfMatch.Success)
        {
            var selfPath = Path.Combine("agents", "main", "SELF.md");
            files[selfPath] = selfMatch.Groups[1].Value.Trim();
        }

        // Extract ### AGENTS.md section
        var agentsMatch = System.Text.RegularExpressions.Regex.Match(
            response,
            @"###\s+AGENTS\.md\s*(.*?)(?=###|\Z)",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (agentsMatch.Success)
        {
            var agentsPath = Path.Combine("agents", "main", "AGENTS.md");
            files[agentsPath] = agentsMatch.Groups[1].Value.Trim();
        }

        return files;
    }

    private static string? MapPathToActionType(string path)
    {
        var upper = path.ToUpperInvariant().Replace('\\', '/');

        if (upper.EndsWith("/USER.MD"))
            return "WriteUserMd";
        if (upper.EndsWith("/SELF.MD"))
            return "WriteSelfMd";
        if (upper.EndsWith("/AGENTS.MD"))
            return "WriteAgentsMd";

        return null;
    }
}

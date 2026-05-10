using System.Text.Json;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Host.Services;

public sealed class EngagementToolExecutionAuthorizer : IToolExecutionAuthorizer
{
    private readonly IActionAuthorizer _actionAuthorizer;

    public EngagementToolExecutionAuthorizer(IActionAuthorizer actionAuthorizer)
    {
        _actionAuthorizer = actionAuthorizer;
    }

    public async Task<ToolExecutionAuthorizationResult> AuthorizeAsync(
        string toolName,
        string parametersJson,
        CancellationToken ct)
    {
        var actionType = MapActionType(toolName, parametersJson);
        if (string.IsNullOrWhiteSpace(actionType))
            return ToolExecutionAuthorizationResult.Allow();

        var result = await _actionAuthorizer.AuthorizeAsync(actionType, ct);
        return result.IsAuthorized
            ? ToolExecutionAuthorizationResult.Allow(actionType)
            : ToolExecutionAuthorizationResult.Deny(result.Reason ?? "Tool execution denied", actionType);
    }

    internal static string? MapActionType(string toolName, string parametersJson)
    {
        var normalizedToolName = toolName.Trim();
        var path = TryGetPath(parametersJson, "path");
        var destinationPath = TryGetPath(parametersJson, "destinationPath");

        return normalizedToolName switch
        {
            "file_read" => "ReadFile",
            "directory_list" => "ListFiles",
            "file_stat" => "StatFile",
            "directory_mkdir" => "CreateDirectory",
            "file_chmod" => "ChangeFilePermissions",
            "file_delete" => "DeleteFile",
            "file_move" => MapWriteAction(destinationPath) ?? "MoveFile",
            "file_copy" => MapWriteAction(destinationPath) ?? "CopyFile",
            "file_write" => MapWriteAction(path) ?? "WriteFile",
            "file_edit" => MapWriteAction(path) ?? "WriteFile",
            "file_touch" => MapWriteAction(path) ?? "WriteFile",
            "search_knowledge" => "SearchKnowledge",
            "wiki_query" => "SearchWiki",
            "web_search" => "SearchWeb",
            _ => null
        };
    }

    internal static string? MapWriteAction(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = path.Replace('\\', '/').TrimStart('/');

        return normalized.ToUpperInvariant() switch
        {
            "SELF.MD" => "WriteSelfMd",
            "USER.MD" => "WriteUserMd",
            "AGENTS/MAIN/AGENTS.MD" => "WriteAgentsMd",
            _ => null
        };
    }

    private static string? TryGetPath(string parametersJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(parametersJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            if (!doc.RootElement.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
                return null;

            return element.GetString();
        }
        catch
        {
            return null;
        }
    }
}
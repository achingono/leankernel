using System.Text.Json;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Thinker.Authorization;

/// <summary>
/// Maps tool calls to engagement-rule action types before execution.
/// </summary>
public sealed class EngagementToolExecutionAuthorizer : IToolExecutionAuthorizer
{
    private readonly IActionAuthorizer _actionAuthorizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="EngagementToolExecutionAuthorizer" /> class.
    /// </summary>
    /// <param name="actionAuthorizer">The action authorizer used to enforce engagement rules.</param>
    public EngagementToolExecutionAuthorizer(IActionAuthorizer actionAuthorizer)
    {
        _actionAuthorizer = actionAuthorizer;
    }

    /// <inheritdoc />
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
            "file_search" => "SearchFiles",
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
            "search_documents" => "SearchKnowledge",
            "search_wiki" => "GetWikiEntry",
            "get_wiki_entry" => "GetWikiEntry",
            "web_search" => "SearchWeb",
            _ => null
        };
    }

    internal static string? MapWriteAction(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = path.Replace('\\', '/').TrimStart('/');
        var upper = normalized.ToUpperInvariant();

        if (upper == "SELF.MD" || upper.EndsWith("/SELF.MD", StringComparison.Ordinal))
            return "WriteSelfMd";

        if (upper == "USER.MD" || upper.EndsWith("/USER.MD", StringComparison.Ordinal))
            return "WriteUserMd";

        if (upper == "AGENTS.MD" || upper.EndsWith("/AGENTS.MD", StringComparison.Ordinal))
            return "WriteAgentsMd";

        return null;
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

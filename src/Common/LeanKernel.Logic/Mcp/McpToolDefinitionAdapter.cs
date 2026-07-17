using System.Text.Json;
using LeanKernel.Logic.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace LeanKernel.Logic.Mcp;

/// <summary>
/// Maps MCP SDK tool types to LeanKernel <see cref="ToolDefinition"/> adapters.
/// </summary>
public static class McpToolDefinitionAdapter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Creates a LeanKernel <see cref="ToolDefinition"/> from an MCP tool discovered via the SDK.
    /// </summary>
    /// <param name="mcpTool">The MCP tool to adapt.</param>
    /// <param name="category">The server name, used as the tool category.</param>
    /// <returns>A LeanKernel tool definition wrapping the MCP tool.</returns>
    public static ToolDefinition CreateToolDefinition(McpClientTool mcpTool, string category)
    {
        ArgumentNullException.ThrowIfNull(mcpTool);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);

        var schema = mcpTool.ProtocolTool.InputSchema;
        var hasSchema = schema.ValueKind != JsonValueKind.Undefined;
        var parameters = hasSchema
            ? ExtractParameters(schema)
            : [];

        return new ToolDefinition
        {
            Name = mcpTool.Name,
            Description = mcpTool.Description ?? $"MCP tool: {mcpTool.Name}",
            Category = category,
            Parameters = parameters,
            Handler = (args, ct) => InvokeMcpToolAsync(mcpTool, args, ct)
        };
    }

    private static async Task<ToolResult> InvokeMcpToolAsync(
        McpClientTool mcpTool,
        IReadOnlyDictionary<string, object?> args,
        CancellationToken ct)
    {
        try
        {
            var result = await mcpTool.CallAsync(args, cancellationToken: ct).ConfigureAwait(false);
            var output = FormatToolResult(result);

            return new ToolResult
            {
                ToolName = mcpTool.Name,
                Success = true,
                Output = output
            };
        }
        catch (McpException ex)
        {
            return new ToolResult
            {
                ToolName = mcpTool.Name,
                Success = false,
                Error = $"MCP error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolName = mcpTool.Name,
                Success = false,
                Error = $"MCP tool invocation failed: {ex.Message}"
            };
        }
    }

    private static string FormatToolResult(CallToolResult result)
    {
        if (result.Content.Count == 0)
        {
            return string.Empty;
        }

        if (result.Content.Count == 1)
        {
            return FormatContentBlock(result.Content[0]);
        }

        var parts = new List<string>(result.Content.Count);
        foreach (var block in result.Content)
        {
            parts.Add(FormatContentBlock(block));
        }

        return JsonSerializer.Serialize(parts, SerializerOptions);
    }

    internal static string FormatContentBlock(ContentBlock block)
    {
        return block.Type switch
        {
            "text" when block is TextContentBlock text => text.Text ?? string.Empty,
            "image" when block is ImageContentBlock image => $"data:{image.MimeType};base64,{image.Data}",
            "resource" when block is EmbeddedResourceBlock resource => resource.Resource?.Uri ?? string.Empty,
            _ => JsonSerializer.Serialize(block, SerializerOptions)
        };
    }

    internal static ToolParameter[] ExtractParameters(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        if (!schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var requiredNames = LoadRequiredNames(schema);
        var parameters = new List<ToolParameter>();

        foreach (var prop in properties.EnumerateObject())
        {
            parameters.Add(new ToolParameter
            {
                Name = prop.Name,
                Type = ReadType(prop.Value),
                Description = ReadDescription(prop.Value),
                Required = requiredNames.Contains(prop.Name)
            });
        }

        return [.. parameters];
    }

    private static HashSet<string> LoadRequiredNames(JsonElement schema)
    {
        if (!schema.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in required.EnumerateArray())
        {
            if (item.GetString() is { } name)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static string ReadType(JsonElement prop)
    {
        if (prop.TryGetProperty("type", out var typeElement) && typeElement.GetString() is { } typeStr)
        {
            return typeStr;
        }

        return "string";
    }

    private static string ReadDescription(JsonElement prop)
    {
        if (prop.TryGetProperty("description", out var descElement) && descElement.GetString() is { } descStr)
        {
            return descStr;
        }

        return string.Empty;
    }
}

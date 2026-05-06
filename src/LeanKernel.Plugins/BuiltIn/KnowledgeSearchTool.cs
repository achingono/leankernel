using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

[ToolMetadata(
    Name = "search_knowledge",
    Description = "Search the knowledge base for relevant documents, wiki facts, and indexed content. Results are scoped to the agent's configured access.",
    Category = ToolCategory.Wiki)]
public sealed class KnowledgeSearchTool : ITool
{
    private readonly IKnowledgeSearchService _knowledge;
    private readonly KnowledgeConfig _config;

    public string Name => "search_knowledge";
    public string Description => "Search the unified knowledge base (wiki + documents) for relevant content.";
    public string Category => ToolCategory.Wiki.ToString().ToLower();
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Search query text" },
            "limit": { "type": "integer", "default": 5, "description": "Maximum results to return (1-50)" },
            "tags": { "type": "array", "items": { "type": "string" }, "description": "Optional knowledge tags to filter by" }
          },
          "required": ["query"]
        }
        """;

    public KnowledgeSearchTool(IKnowledgeSearchService knowledge, IOptions<LeanKernelConfig> config)
    {
        _knowledge = knowledge;
        _config = config.Value.Knowledge;
    }

    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var agentTags = _config.DefaultDocumentTags.ToList();
            agentTags.Add("wiki"); // Always include wiki access
            var limit = 5;
            string query = parametersJson;

            // Parse parameters
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(parametersJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("query", out var queryProp))
                    query = queryProp.GetString() ?? parametersJson;
                if (root.TryGetProperty("limit", out var limitProp) && limitProp.TryGetInt32(out var l))
                    limit = Math.Clamp(l, 1, 50);
                if (root.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    agentTags = tagsProp.EnumerateArray()
                        .Select(t => t.GetString() ?? "")
                        .Where(t => !string.IsNullOrEmpty(t))
                        .ToList();
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // parametersJson is plain text query, use as-is
            }

            var results = await _knowledge.SearchAsync(query, agentTags, limit, ct);

            var output = results.Count > 0
                ? string.Join("\n\n", results.Select((r, i) =>
                    $"[{i + 1}] (score: {r.Score:F2}) {r.Content}"))
                : "No matching knowledge found.";

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
}

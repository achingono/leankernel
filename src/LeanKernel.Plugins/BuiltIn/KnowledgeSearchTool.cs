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

    public string Name => "search_knowledge";
    public string Description => "Search the unified knowledge base (wiki + documents) for relevant content.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Search query text" },
            "limit": { "type": "integer", "default": 5, "description": "Maximum results to return" }
          },
          "required": ["query"]
        }
        """;

    public KnowledgeSearchTool(IKnowledgeSearchService knowledge)
    {
        _knowledge = knowledge;
    }

    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Default agent scope: wiki access (callers can override via context)
            var agentTags = new List<string> { "wiki" };
            var limit = 5;

            // Simple parameter extraction
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(parametersJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("query", out var queryProp))
                    parametersJson = queryProp.GetString() ?? parametersJson;
                if (root.TryGetProperty("limit", out var limitProp) && limitProp.TryGetInt32(out var l))
                    limit = l;
            }
            catch (System.Text.Json.JsonException)
            {
                // parametersJson is plain text query, use as-is
            }

            var results = await _knowledge.SearchAsync(parametersJson, agentTags, limit, ct);

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

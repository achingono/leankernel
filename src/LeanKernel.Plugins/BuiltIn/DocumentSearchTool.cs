using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Tool that searches indexed reference documents only.
/// </summary>
[ToolMetadata(
    Name = "search_documents",
    Description = "Search indexed reference documents and return relevant passages.",
    Category = ToolCategory.Wiki)]
public sealed class DocumentSearchTool : ITool
{
    private readonly IKnowledgeSearchService _knowledge;
    private readonly KnowledgeConfig _config;

    public DocumentSearchTool(IKnowledgeSearchService knowledge, IOptions<LeanKernelConfig> config)
    {
        _knowledge = knowledge;
        _config = config.Value.Knowledge;
    }

    public string Name => "search_documents";
    public string Description => "Search indexed documents (books, papers, notes) without wiki facts.";
    public string Category => ToolCategory.Wiki.ToString().ToLowerInvariant();
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Search query text" },
            "maxResults": { "type": "integer", "default": 5, "minimum": 1, "maximum": 20 },
            "tags": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["query"]
        }
        """;

    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (!TryParse(parametersJson, out var query, out var maxResults, out var tags, out var error))
            {
                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = error,
                    Duration = sw.Elapsed
                };
            }

            var effectiveTags = tags.Count > 0 ? tags : _config.DefaultDocumentTags.ToList();
            var results = await _knowledge.SearchAsync(query, effectiveTags, maxResults, ct, sourceType: "document");
            var output = results.Count > 0
                ? string.Join("\n\n", results.Select((r, i) => $"[{i + 1}] (score: {r.Score:F2}) {r.Content}"))
                : "No matching documents found.";

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

    private static bool TryParse(
        string parametersJson,
        out string query,
        out int maxResults,
        out List<string> tags,
        out string error)
    {
        query = string.Empty;
        maxResults = 5;
        tags = [];
        error = string.Empty;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(parametersJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("query", out var queryElement))
            {
                error = "Missing required parameter: query";
                return false;
            }

            query = queryElement.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                error = "Parameter 'query' cannot be blank.";
                return false;
            }

            if (root.TryGetProperty("maxResults", out var maxResultsElement) &&
                maxResultsElement.TryGetInt32(out var parsedMax))
            {
                maxResults = Math.Clamp(parsedMax, 1, 20);
            }

            if (root.TryGetProperty("tags", out var tagsElement) &&
                tagsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                tags = tagsElement
                    .EnumerateArray()
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
                    .ToList();
            }

            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            error = "Invalid parameters JSON. Expected {\"query\":\"...\",\"maxResults\":N,\"tags\":[...]}";
            return false;
        }
    }
}


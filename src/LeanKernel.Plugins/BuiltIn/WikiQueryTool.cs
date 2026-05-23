using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Represents the wiki query tool.
/// </summary>
[ToolMetadata(
    Name = "search_wiki",
    Description = "Semantic search over wiki content only. Use for wiki-only requests like 'search the wiki', personal notes, profile facts, preferences, people, projects, and 5W1H memory.",
    Category = ToolCategory.Wiki)]
public sealed class WikiQueryTool : ITool
{
    private readonly IKnowledgeSearchService _knowledge;
    private readonly KnowledgeConfig _config;

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name => "search_wiki";
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description =>
        "Semantic wiki-only search over indexed memory. Use for requests like " +
        "'search the wiki', 'search your notes', 'what do you know about', 'do you remember', " +
        "'look up in the wiki', and 'find wiki facts'.";
    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public string Category => ToolCategory.Wiki.ToString().ToLower();
    /// <summary>
    /// Gets or sets the parameters schema.
    /// </summary>
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Search text" },
            "maxResults": { "type": "integer", "default": 5, "minimum": 1, "maximum": 20 },
            "tags": { "type": "array", "items": { "type": "string" }, "description": "Optional additional tag filters" }
          },
          "required": ["query"]
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="WikiQueryTool" /> class.
    /// </summary>
    /// <param name="knowledge">The knowledge.</param>
    /// <param name="config">The config.</param>
    public WikiQueryTool(IKnowledgeSearchService knowledge, IOptions<LeanKernelConfig> config)
    {
        _knowledge = knowledge;
        _config = config.Value.Knowledge;
    }

    /// <summary>
    /// Executes the execute async operation.
    /// </summary>
    /// <param name="parametersJson">The parameters json.</param>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (!TryParseParameters(parametersJson, out var query, out var maxResults, out var tags, out var parseError))
            {
                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = parseError,
                    Duration = sw.Elapsed
                };
            }

            var effectiveTags = tags.Count > 0 ? tags : _config.DefaultDocumentTags.ToList();
            if (!effectiveTags.Contains("wiki", StringComparer.OrdinalIgnoreCase))
            {
                effectiveTags.Add("wiki");
            }

            var results = await _knowledge.SearchAsync(query, effectiveTags, maxResults, ct, sourceType: "wiki");
            var output = results.Count > 0
                ? string.Join("\n\n", results.Select((r, i) =>
                    $"[{i + 1}] (score: {r.Score:F2}) {r.Content}"))
                : "No matching wiki content found.";

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

    private static bool TryParseParameters(
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
                    .Select(element => element.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!)
                    .ToList();
            }

            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            error = "Invalid parameters JSON. Expected {\"query\":\"...\",\"maxResults\":N,\"tags\":[...]}.";
            return false;
        }
    }
}

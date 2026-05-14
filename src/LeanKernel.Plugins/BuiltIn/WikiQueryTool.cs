using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Represents the wiki query tool.
/// </summary>
[ToolMetadata(
    Name = "search_wiki",
    Description = "Search personal wiki facts about people, projects, preferences, and process context.",
    Category = ToolCategory.Wiki)]
public sealed class WikiQueryTool : ITool
{
    private readonly IWikiStore _wiki;

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name => "search_wiki";
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description => "Search the 5W1H knowledge wiki for stored facts.";
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
            "dimensions": { "type": "array", "items": { "type": "string", "enum": ["who","what","where","when","why","how"] } },
            "maxResults": { "type": "integer", "default": 5 }
          },
          "required": ["query"]
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="WikiQueryTool" /> class.
    /// </summary>
    /// <param name="wiki">The wiki.</param>
    public WikiQueryTool(IWikiStore wiki)
    {
        _wiki = wiki;
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
            if (!TryParseParameters(parametersJson, out var query, out var parseError))
            {
                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = parseError,
                    Duration = sw.Elapsed
                };
            }

            var results = await _wiki.QueryAsync(query, ct);
            var output = string.Join("\n", results.Select(r =>
                $"[{r.Dimension}:{r.Subject}] {string.Join("; ", r.Facts.Select(f => f.Claim))}"));

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = results.Count > 0 ? output : "No matching wiki entries found.",
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

    private static bool TryParseParameters(string parametersJson, out WikiQuery query, out string error)
    {
        var queryText = string.Empty;
        var maxResults = 5;
        var dimensions = new HashSet<Core.Enums.WikiDimension>();
        error = string.Empty;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(parametersJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("query", out var queryElement))
            {
                error = "Missing required parameter: query";
                query = new WikiQuery { TextQuery = string.Empty, MaxResults = 5 };
                return false;
            }

            queryText = queryElement.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(queryText))
            {
                error = "Parameter 'query' cannot be blank.";
                query = new WikiQuery { TextQuery = string.Empty, MaxResults = 5 };
                return false;
            }

            if (root.TryGetProperty("maxResults", out var maxResultsElement) &&
                maxResultsElement.TryGetInt32(out var parsedMax))
            {
                maxResults = Math.Clamp(parsedMax, 1, 20);
            }

            if (root.TryGetProperty("dimensions", out var dimensionsElement) &&
                dimensionsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var element in dimensionsElement.EnumerateArray())
                {
                    if (Enum.TryParse<Core.Enums.WikiDimension>(element.GetString(), true, out var dimension))
                    {
                        dimensions.Add(dimension);
                    }
                }
            }

            query = new WikiQuery
            {
                TextQuery = queryText,
                MaxResults = maxResults,
                Dimensions = dimensions
            };
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            error = "Invalid parameters JSON. Expected {\"query\":\"...\",\"dimensions\":[...],\"maxResults\":N}.";
            query = new WikiQuery { TextQuery = string.Empty, MaxResults = 5 };
            return false;
        }
    }
}

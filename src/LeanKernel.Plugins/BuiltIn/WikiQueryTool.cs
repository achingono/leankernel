using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Represents the wiki query tool.
/// </summary>
[ToolMetadata(
    Name = "wiki_query",
    Description = "Search the 5W1H knowledge wiki for stored facts about people, events, places, times, reasons, or processes.",
    Category = ToolCategory.Wiki)]
public sealed class WikiQueryTool : ITool
{
    private readonly IWikiStore _wiki;

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name => "wiki_query";
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
            var query = new WikiQuery { TextQuery = parametersJson, MaxResults = 5 };
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
}

using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Web search tool using DuckDuckGo Instant Answer API (no API key needed).
/// Falls back to a basic search when the full API is unavailable.
/// </summary>
[ToolMetadata(
    Name = "web_search",
    Description = "Search the web for current information using DuckDuckGo.",
    Category = ToolCategory.Information)]
public sealed class WebSearchTool : ITool
{
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name => "web_search";
    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description => "Search the web for current information.";
    /// <summary>
    /// Gets or sets the category.
    /// </summary>
    public string Category => ToolCategory.Information.ToString().ToLower();
    /// <summary>
    /// Gets or sets the parameters schema.
    /// </summary>
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Search query" }
          },
          "required": ["query"]
        }
        """;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebSearchTool" /> class.
    /// </summary>
    /// <param name="httpClient">The http client.</param>
    public WebSearchTool(HttpClient httpClient)
    {
        _httpClient = httpClient;
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
            var query = ExtractQuery(parametersJson);
            var encoded = Uri.EscapeDataString(query);
            var url = $"https://api.duckduckgo.com/?q={encoded}&format=json&no_redirect=1&no_html=1";

            var response = await _httpClient.GetStringAsync(url, ct);
            var doc = JsonDocument.Parse(response);

            var abstractText = doc.RootElement.TryGetProperty("AbstractText", out var at) ? at.GetString() : null;
            var answer = doc.RootElement.TryGetProperty("Answer", out var ans) ? ans.GetString() : null;

            var result = GetBestAnswer(query, abstractText, answer);

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = result,
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

    private static string ExtractQuery(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("query", out var q) ? q.GetString() ?? json : json;
        }
        catch
        {
            return json;
        }
    }

    private static string GetBestAnswer(string query, string? abstractText, string? answer)
    {
        if (!string.IsNullOrEmpty(abstractText))
            return abstractText;

        return !string.IsNullOrEmpty(answer)
            ? answer
            : $"No instant answer found for: {query}";
    }
}

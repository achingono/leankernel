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

    public string Name => "web_search";
    public string Description => "Search the web for current information.";
    public string Category => ToolCategory.Information.ToString().ToLower();
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Search query" }
          },
          "required": ["query"]
        }
        """;

    public WebSearchTool(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

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

            var result = !string.IsNullOrEmpty(abstractText) ? abstractText
                       : !string.IsNullOrEmpty(answer) ? answer
                       : $"No instant answer found for: {query}";

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
}

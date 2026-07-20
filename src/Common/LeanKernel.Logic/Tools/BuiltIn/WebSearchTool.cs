using System.Text;
using System.Text.Json;

using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Tools.BuiltIn;

/// <summary>
/// Provides the LeanKernel-owned <c>web_search</c> built-in tool.
/// Queries Brave when the configured API key environment variable is set,
/// and falls back to DuckDuckGo otherwise.
/// </summary>
public static class WebSearchTool
{
    private const string ToolName = "web_search";

    /// <summary>
    /// Creates the web_search tool definition backed by the scoped DI scope factory.
    /// </summary>
    /// <param name="scopeFactory">Factory used to create a DI scope per invocation.</param>
    /// <returns>The configured tool definition.</returns>
    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Search the web for relevant information using Brave or DuckDuckGo",
            Category = "internet",
            Parameters =
            [
                new ToolParameter
                {
                    Name = "query",
                    Type = "string",
                    Description = "The search query",
                    Required = true
                }
            ],
            Handler = async (args, ct) =>
            {
                var query = ToolArgumentReader.GetString(args, "query");
                if (string.IsNullOrWhiteSpace(query))
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = "query is required" };
                }

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var settings = scope.ServiceProvider.GetRequiredService<IOptions<AgentSettings>>().Value;
                    var webSearch = settings.Tools.WebSearch;

                    var factory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
                    var client = factory.CreateClient("web-search");

                    var braveKey = Environment.GetEnvironmentVariable(webSearch.ApiKeyEnv);
                    var output = !string.IsNullOrWhiteSpace(braveKey)
                        ? await SearchWithFallbackAsync(client, query, braveKey, ct).ConfigureAwait(false)
                        : await SearchWithDuckDuckGoAsync(client, query, ct).ConfigureAwait(false);

                    return new ToolResult { ToolName = ToolName, Success = true, Output = output };
                }
                catch (Exception ex)
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = ex.Message };
                }
            }
        };
    }

    private static async Task<string> SearchWithFallbackAsync(
        HttpClient client, string query, string apiKey, CancellationToken ct)
    {
        try
        {
            return await SearchWithBraveAsync(client, query, apiKey, ct).ConfigureAwait(false);
        }
        catch
        {
            return await SearchWithDuckDuckGoAsync(client, query, ct).ConfigureAwait(false);
        }
    }

    private static async Task<string> SearchWithBraveAsync(
        HttpClient client, string query, string apiKey, CancellationToken ct)
    {
        var encoded = Uri.EscapeDataString(query);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.search.brave.com/res/v1/web/search?q={encoded}&count=10");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-Subscription-Token", apiKey);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        var results = ParseBraveResults(doc);
        return results.Count == 0 ? "No results found." : string.Join("\n", results);
    }

    private static List<string> ParseBraveResults(JsonDocument doc)
    {
        var results = new List<string>();
        if (!doc.RootElement.TryGetProperty("web", out var web) ||
            !web.TryGetProperty("results", out var webResults))
        {
            return results;
        }

        foreach (var item in webResults.EnumerateArray().Take(5))
        {
            var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
            var description = item.TryGetProperty("description", out var d) ? d.GetString() : null;
            var url = item.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (!string.IsNullOrWhiteSpace(title))
            {
                results.Add($"- {title}: {description} ({url})");
            }
        }

        return results;
    }

    private static async Task<string> SearchWithDuckDuckGoAsync(
        HttpClient client, string query, CancellationToken ct)
    {
        var encoded = Uri.EscapeDataString(query);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.duckduckgo.com/?q={encoded}&format=json&no_redirect=1&no_html=1");

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);

        var sb = new StringBuilder();

        if (doc.RootElement.TryGetProperty("AbstractText", out var at) &&
            at.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(at.GetString()))
        {
            sb.AppendLine(at.GetString());
        }

        if (doc.RootElement.TryGetProperty("Answer", out var ans) &&
            ans.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(ans.GetString()))
        {
            sb.AppendLine(ans.GetString());
        }

        if (doc.RootElement.TryGetProperty("RelatedTopics", out var topics))
        {
            foreach (var topic in topics.EnumerateArray().Take(5))
            {
                if (topic.TryGetProperty("Text", out var text) &&
                    !string.IsNullOrWhiteSpace(text.GetString()))
                {
                    sb.AppendLine($"- {text.GetString()}");
                }
            }
        }

        var result = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? "No results found." : result;
    }
}
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools.BuiltIn.Common;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Tools.BuiltIn.Internet;

/// <summary>
/// Built-in tool: searches the web via DuckDuckGo.
/// </summary>
public static class WebSearchTool
{
    private const string ToolName = "web_search";

    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        return new ToolDefinition
        {
            Name = ToolName,
            Description = "Search the web for relevant information",
            Category = "internet",
            Parameters =
            [
                new ToolParameter { Name = "query", Type = "string", Description = "Search query", Required = true },
            ],
            Handler = async (args, ct) =>
            {
                var query = ToolArgumentReader.GetString(args, "query");

                if (string.IsNullOrWhiteSpace(query))
                {
                    return new ToolResult { ToolName = ToolName, Success = false, Error = "Query is required" };
                }

                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var client = scope.ServiceProvider.GetRequiredService<HttpClient>();
                    var braveApiKey = Environment.GetEnvironmentVariable("BRAVE_API_KEY");

                    var output = !string.IsNullOrWhiteSpace(braveApiKey)
                        ? await SearchWithBraveAsync(client, query, braveApiKey, ct)
                        : await SearchWithDuckDuckGoAsync(client, query, ct);

                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = true,
                        Output = output
                    };
                }
                catch (Exception ex)
                {
                    return new ToolResult
                    {
                        ToolName = ToolName,
                        Success = false,
                        Error = ex.Message
                    };
                }
            }
        };
    }

    private static async Task<string> SearchWithDuckDuckGoAsync(HttpClient client, string query, CancellationToken ct)
    {
        var encoded = Uri.EscapeDataString(query);
        var url = $"https://api.duckduckgo.com/?q={encoded}&format=json&no_redirect=1&no_html=1";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"DuckDuckGo request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        var abstractText = doc.RootElement.TryGetProperty("AbstractText", out var at) ? at.GetString() : null;
        var answer = doc.RootElement.TryGetProperty("Answer", out var ans) ? ans.GetString() : null;

        return GetBestDuckDuckGoAnswer(query, abstractText, answer);
    }

    [SuppressMessage("Major Code Smell", "S3776", Justification = "Fallback search parsing is intentionally explicit.")]
    private static async Task<string> SearchWithBraveAsync(HttpClient client, string query, string apiKey, CancellationToken ct)
    {
        var encoded = Uri.EscapeDataString(query);
        var url = $"https://api.search.brave.com/res/v1/web/search?q={encoded}&count=5";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("X-Subscription-Token", apiKey);

        using var response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Brave search request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        if (!doc.RootElement.TryGetProperty("web", out var web)
            || !web.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array)
        {
            return $"No search results found for: {query}";
        }

        var builder = new StringBuilder();
        foreach (var item in results.EnumerateArray())
        {
            var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
            var description = item.TryGetProperty("description", out var d) ? d.GetString() : null;
            var link = item.TryGetProperty("url", out var u) ? u.GetString() : null;

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description) && string.IsNullOrWhiteSpace(link))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                builder.AppendLine(title);
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                builder.AppendLine(description);
            }

            if (!string.IsNullOrWhiteSpace(link))
            {
                builder.AppendLine(link);
            }

            builder.AppendLine();
        }

        return builder.Length > 0
            ? builder.ToString().TrimEnd()
            : $"No search results found for: {query}";
    }


    private static string GetBestDuckDuckGoAnswer(string query, string? abstractText, string? answer)
    {
        if (!string.IsNullOrEmpty(abstractText))
            return abstractText;

        return !string.IsNullOrEmpty(answer)
            ? answer
            : $"No instant answer found for: {query}";
    }

}

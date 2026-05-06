using System.Text.Json;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn.OpenclaSkills;

/// <summary>
/// Emanate social media automation skill — create, schedule, and publish posts
/// across Twitter/X, LinkedIn, Facebook, and Reddit.
/// </summary>
[ToolMetadata(
    Name = "emanate_skill",
    Description = "Emanate social media automation API: create, schedule, and publish posts across Twitter/X, LinkedIn, Facebook, and Reddit. Use for drafting, scheduling, publishing posts, or checking engagement analytics.",
    Category = ToolCategory.General)]
public sealed class EmanateSkillTool : ITool
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl = "http://host.docker.internal:3000";

    public string Name => "emanate_skill";
    public string Description => "Manage social media posts via Emanate API.";
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "operation": { 
              "type": "string", 
              "description": "Operation to perform: list_platforms, create_draft, generate_post, schedule_post, publish_now, list_posts, get_post, delete_post, engagement_summary",
              "enum": ["list_platforms", "create_draft", "generate_post", "schedule_post", "publish_now", "list_posts", "get_post", "delete_post", "engagement_summary"]
            },
            "platform_id": { "type": "string", "description": "Platform ID (from list_platforms)" },
            "content": { "type": "string", "description": "Post content text" },
            "prompt": { "type": "string", "description": "AI generation prompt" },
            "content_type": { "type": "string", "description": "text, thread, poll, or article" },
            "post_id": { "type": "string", "description": "Post ID" },
            "scheduled_at": { "type": "string", "description": "ISO 8601 datetime for scheduling" },
            "timezone": { "type": "string", "description": "IANA timezone (e.g., America/Edmonton)" },
            "model": { "type": "string", "description": "LLM model for generation" },
            "days": { "type": "integer", "description": "Number of days for analytics summary" }
          },
          "required": ["operation"]
        }
        """;

    public EmanateSkillTool(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var doc = JsonDocument.Parse(parametersJson);
            var root = doc.RootElement;
            var operation = root.GetProperty("operation").GetString() ?? "";

            var result = operation switch
            {
                "list_platforms" => await ListPlatforms(ct),
                "create_draft" => await CreateDraft(root, ct),
                "generate_post" => await GeneratePost(root, ct),
                "schedule_post" => await SchedulePost(root, ct),
                "publish_now" => await PublishNow(root, ct),
                "list_posts" => await ListPosts(root, ct),
                "get_post" => await GetPost(root, ct),
                "delete_post" => await DeletePost(root, ct),
                "engagement_summary" => await GetEngagementSummary(root, ct),
                _ => $"Unknown operation: {operation}"
            };

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

    private async Task<string> ListPlatforms(CancellationToken ct)
    {
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/platforms", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> CreateDraft(JsonElement root, CancellationToken ct)
    {
        var platformId = root.GetProperty("platform_id").GetString();
        var content = root.GetProperty("content").GetString();
        var contentType = root.GetProperty("content_type").GetString() ?? "text";

        var payload = new { platform_id = platformId, content, content_type = contentType };
        var json = JsonSerializer.Serialize(payload);
        var content_obj = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}/api/posts", content_obj, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> GeneratePost(JsonElement root, CancellationToken ct)
    {
        var platformId = root.GetProperty("platform_id").GetString();
        var prompt = root.GetProperty("prompt").GetString();
        var contentType = root.GetProperty("content_type").GetString() ?? "text";
        var model = root.TryGetProperty("model", out var modelElem) ? modelElem.GetString() : null;

        var payload = new { platform_id = platformId, prompt, content_type = contentType, model };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}/api/posts/generate", content, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> SchedulePost(JsonElement root, CancellationToken ct)
    {
        var postId = root.GetProperty("post_id").GetString();
        var scheduledAt = root.GetProperty("scheduled_at").GetString();
        var timezone = root.TryGetProperty("timezone", out var tzElem) ? tzElem.GetString() : "UTC";

        var payload = new { scheduled_at = scheduledAt, timezone };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}/api/posts/{postId}/schedule", content, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> PublishNow(JsonElement root, CancellationToken ct)
    {
        var postId = root.GetProperty("post_id").GetString();
        var response = await _httpClient.PostAsync($"{_baseUrl}/api/posts/{postId}/publish-now", null, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> ListPosts(JsonElement root, CancellationToken ct)
    {
        var query = "?limit=10&offset=0";
        if (root.TryGetProperty("status", out var statusElem))
            query = $"?status={statusElem.GetString()}";

        var response = await _httpClient.GetAsync($"{_baseUrl}/api/posts{query}", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> GetPost(JsonElement root, CancellationToken ct)
    {
        var postId = root.GetProperty("post_id").GetString();
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/posts/{postId}", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> DeletePost(JsonElement root, CancellationToken ct)
    {
        var postId = root.GetProperty("post_id").GetString();
        var response = await _httpClient.DeleteAsync($"{_baseUrl}/api/posts/{postId}", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<string> GetEngagementSummary(JsonElement root, CancellationToken ct)
    {
        var days = root.TryGetProperty("days", out var daysElem) ? daysElem.GetInt32() : 7;
        var response = await _httpClient.GetAsync($"{_baseUrl}/api/logs/summary?days={days}", ct);
        return await response.Content.ReadAsStringAsync(ct);
    }
}

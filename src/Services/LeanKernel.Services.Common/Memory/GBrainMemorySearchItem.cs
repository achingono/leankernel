using System.Text.Json.Serialization;

namespace LeanKernel.Services.Common.Memory;

/// <summary>
/// Represents a single memory search result item returned by GBrain.
/// </summary>
internal sealed class GBrainMemorySearchItem
{
    [JsonPropertyName("slug")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("compiled_truth")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("chunk_text")]
    public string ChunkText { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string RawContent { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; set; }

    public string GetBestContent()
    {
        if (!string.IsNullOrWhiteSpace(this.Content))
        {
            return this.Content;
        }

        if (!string.IsNullOrWhiteSpace(this.ChunkText))
        {
            return this.ChunkText;
        }

        if (!string.IsNullOrWhiteSpace(this.RawContent))
        {
            return this.RawContent;
        }

        return this.Title;
    }
}
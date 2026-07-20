using System.Text.Json;
using System.Text.Json.Serialization;

using LeanKernel.Entities;
using LeanKernel.Logic.Providers;

namespace LeanKernel.Gateway.Memory;

/// <summary>
/// Represents the top-level payload returned by GBrain memory search responses.
/// </summary>
internal sealed class GBrainMemorySearchResult
{
    [JsonPropertyName("results")]
    public List<GBrainMemorySearchItem>? Results { get; set; }
}

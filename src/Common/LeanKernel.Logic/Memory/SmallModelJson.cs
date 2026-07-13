using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeanKernel.Logic.Memory;

/// <summary>
/// Centralizes JSON serializer options used when communicating with the small reasoning model.
/// </summary>
internal static class SmallModelJson
{
    /// <summary>
    /// The shared serializer options for strict JSON requests and responses.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

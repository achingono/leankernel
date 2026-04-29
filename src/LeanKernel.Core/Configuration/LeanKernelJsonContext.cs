using System.Text.Json;
using System.Text.Json.Serialization;
using LeanKernel.Core.Models;

namespace LeanKernel.Core.Configuration;

/// <summary>
/// System.Text.Json source generation context for all LeanKernel models.
/// Eliminates runtime reflection for serialization — keeps the binary lean.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WikiEntry))]
[JsonSerializable(typeof(WikiFact))]
[JsonSerializable(typeof(WikiQuery))]
[JsonSerializable(typeof(LeanKernelMessage))]
[JsonSerializable(typeof(ContextBudget))]
[JsonSerializable(typeof(RelevanceScore))]
[JsonSerializable(typeof(ToolResult))]
[JsonSerializable(typeof(ConversationContext))]
[JsonSerializable(typeof(ConversationTurn))]
[JsonSerializable(typeof(List<WikiEntry>))]
[JsonSerializable(typeof(List<ConversationTurn>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class LeanKernelJsonContext : JsonSerializerContext;

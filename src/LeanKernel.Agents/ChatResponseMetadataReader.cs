using Microsoft.Extensions.AI;
using System.Reflection;

namespace LeanKernel.Agents;

/// <summary>
/// Utility class for reading token usage from chat responses.
/// </summary>
public static class ChatResponseMetadataReader
{
    /// <summary>
    /// Extracts the total number of tokens used from a chat response.
    /// </summary>
    /// <param name="response">The chat response to inspect.</param>
    /// <returns>The total number of tokens used.</returns>
    public static int GetTokensUsed(ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        // Some test doubles (and potentially some model response types) may hide ChatResponse.Usage using `new`.
        // `GetProperty("Usage")` then becomes ambiguous; prefer the property declared on the most derived type.
        object? usage = null;
        var responseType = response.GetType();
        var usageProp = responseType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => string.Equals(p.Name, "Usage", System.StringComparison.Ordinal)
                && p.DeclaringType == responseType)
            ?? responseType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => string.Equals(p.Name, "Usage", System.StringComparison.Ordinal));
        if (usageProp is not null)
        {
            usage = usageProp.GetValue(response);
        }
        if (usage is null)
        {
            return 0;
        }

        if (TryReadInt64(usage, "TotalTokenCount", out var totalTokenCount))
        {
            return ToInt32(totalTokenCount);
        }

        if (TryReadInt64(usage, "TotalTokens", out var totalTokens))
        {
            return ToInt32(totalTokens);
        }

        var inputTokens = TryReadInt64(usage, "InputTokenCount", out var inputTokenCount)
            ? inputTokenCount
            : TryReadInt64(usage, "InputTokens", out var inputTokensValue)
                ? inputTokensValue
                : 0;
        var outputTokens = TryReadInt64(usage, "OutputTokenCount", out var outputTokenCount)
            ? outputTokenCount
            : TryReadInt64(usage, "OutputTokens", out var outputTokensValue)
                ? outputTokensValue
                : 0;

        return ToInt32(inputTokens + outputTokens);
    }

    private static bool TryReadInt64(object instance, string propertyName, out long value)
    {
        var property = instance.GetType().GetProperty(propertyName);
        if (property?.GetValue(instance) is int intValue)
        {
            value = intValue;
            return true;
        }

        if (property?.GetValue(instance) is long longValue)
        {
            value = longValue;
            return true;
        }

        value = 0;
        return false;
    }

    private static int ToInt32(long value)
        => value switch
        {
            > int.MaxValue => int.MaxValue,
            < int.MinValue => int.MinValue,
            _ => (int)value
        };
}

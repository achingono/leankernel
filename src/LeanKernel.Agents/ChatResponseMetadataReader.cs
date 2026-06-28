using Microsoft.Extensions.AI;

namespace LeanKernel.Agents;

public static class ChatResponseMetadataReader
{
    public static int GetTokensUsed(ChatResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var usage = response.GetType().GetProperty("Usage")?.GetValue(response);
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

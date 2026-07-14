using System.Text.Json;

namespace LeanKernel.Logic.Tools;

/// <summary>
/// Utility helpers for extracting typed values from untyped tool argument dictionaries.
/// </summary>
public static class ToolArgumentReader
{
    /// <summary>
    /// Returns a string value from the argument dictionary, or null when missing.
    /// </summary>
    public static string? GetString(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var raw))
        {
            return null;
        }

        return raw switch
        {
            string s => s,
            JsonElement el => el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString(),
            null => null,
            _ => raw.ToString()
        };
    }

    /// <summary>
    /// Returns an integer value from the argument dictionary, or null when missing or unparseable.
    /// </summary>
    public static int? GetInt(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var raw))
        {
            return null;
        }

        return raw switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            JsonElement el when el.ValueKind == JsonValueKind.Number => el.TryGetInt32(out var n) ? n : null,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>
    /// Returns a boolean value from the argument dictionary, or null when missing or unparseable.
    /// </summary>
    public static bool? GetBool(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var raw))
        {
            return null;
        }

        return raw switch
        {
            bool b => b,
            JsonElement el when el.ValueKind == JsonValueKind.True => true,
            JsonElement el when el.ValueKind == JsonValueKind.False => false,
            string s when bool.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>
    /// Returns a double value from the argument dictionary, or null when missing or unparseable.
    /// </summary>
    public static double? GetDouble(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var raw))
        {
            return null;
        }

        return raw switch
        {
            double d => d,
            int i => (double)i,
            long l => (double)l,
            JsonElement el when el.ValueKind == JsonValueKind.Number =>
                el.TryGetDouble(out var d) ? d : null,
            string s when double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    /// <summary>
    /// Returns a JSON array or object string value for structured inputs, or null when missing.
    /// </summary>
    public static string? GetJson(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var raw))
        {
            return null;
        }

        return raw switch
        {
            JsonElement el => el.GetRawText(),
            string s => s,
            null => null,
            _ => System.Text.Json.JsonSerializer.Serialize(raw)
        };
    }
}

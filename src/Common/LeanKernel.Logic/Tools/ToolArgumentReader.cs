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

    /// <summary>
    /// Returns a string value or empty string when missing (source-compatible).
    /// </summary>
    public static string GetStringOrEmpty(IReadOnlyDictionary<string, object?> args, string key)
    {
        return GetString(args, key) ?? string.Empty;
    }

    /// <summary>
    /// Returns an integer value with a default fallback (source-compatible).
    /// </summary>
    public static int GetInt32OrDefault(IReadOnlyDictionary<string, object?> args, string key, int defaultValue)
    {
        return GetInt(args, key) ?? defaultValue;
    }

    /// <summary>
    /// Returns a boolean value with a default fallback (source-compatible).
    /// </summary>
    public static bool GetBoolOrDefault(IReadOnlyDictionary<string, object?> args, string key, bool defaultValue)
    {
        return GetBool(args, key) ?? defaultValue;
    }

    /// <summary>
    /// Returns a string map from the argument dictionary, or an empty dictionary when missing.
    /// </summary>
    public static IReadOnlyDictionary<string, string> GetStringDictionary(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var raw) || raw is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        if (raw is IDictionary<string, string> typedMap)
        {
            return new Dictionary<string, string>(typedMap, StringComparer.Ordinal);
        }

        if (raw is IDictionary<string, object?> objectMap)
        {
            return objectMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? string.Empty, StringComparer.Ordinal);
        }

        if (raw is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.Null => string.Empty,
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    _ => property.Value.ToString() ?? string.Empty
                };
            }

            return result;
        }

        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns an object dictionary from the argument dictionary, or an empty dictionary when missing.
    /// </summary>
    public static Dictionary<string, object?> GetObjectDictionary(IReadOnlyDictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var raw) || raw is null)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        if (raw is Dictionary<string, object?> dictionary)
        {
            return new Dictionary<string, object?>(dictionary, StringComparer.Ordinal);
        }

        if (raw is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            return readOnlyDictionary.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }

        if (raw is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                result[property.Name] = ConvertJsonValue(property.Value);
            }

            return result;
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    private static object? ConvertJsonValue(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetInt64(out var int64Value) ? int64Value : (object)value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }
}
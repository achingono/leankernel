using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LeanKernel.Tools.BuiltIn.Common;

/// <summary>
/// Provides functionality for tool argument reader.
/// </summary>
internal static class ToolArgumentReader
{
    /// <summary>
    /// Gets string.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="name">The name.</param>
    /// <returns>The operation result.</returns>
    public static string GetString(IDictionary<string, object?> arguments, string name)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return string.Empty;
        }

        return value switch
        {
            JsonElement element when element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
            _ => value.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// Gets int32 or default.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="name">The name.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <returns>The operation result.</returns>
    public static int GetInt32OrDefault(IDictionary<string, object?> arguments, string name, int defaultValue)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return defaultValue;
        }

        var parsed = value switch
        {
            int intValue => intValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            JsonElement element when TryGetJsonInt32(element, out var jsonValue) => jsonValue,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stringValue) => stringValue,
            _ when TryConvertToInt32(value, out var convertedValue) => convertedValue,
            _ => defaultValue
        };

        return parsed;
    }

    /// <summary>
    /// Gets bool or default.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="name">The name.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <returns>The operation result.</returns>
    public static bool GetBoolOrDefault(IDictionary<string, object?> arguments, string name, bool defaultValue)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return defaultValue;
        }

        return value switch
        {
            bool boolValue => boolValue,
            JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
            string text when bool.TryParse(text, out var stringValue) => stringValue,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Gets json object or null.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="name">The name.</param>
    /// <returns>The operation result.</returns>
    public static JsonObject? GetJsonObjectOrNull(IDictionary<string, object?> arguments, string name)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            JsonObject jsonObject => jsonObject,
            JsonNode jsonNode => jsonNode as JsonObject,
            JsonElement element when element.ValueKind == JsonValueKind.Object => JsonNode.Parse(element.GetRawText()) as JsonObject,
            string text when TryParseJsonNode(text, out var node) => node as JsonObject,
            IDictionary<string, object?> map => JsonSerializer.SerializeToNode(map) as JsonObject,
            IDictionary<string, string> map => JsonSerializer.SerializeToNode(map) as JsonObject,
            _ => JsonSerializer.SerializeToNode(value) as JsonObject
        };
    }

    /// <summary>
    /// Gets json array or null.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="name">The name.</param>
    /// <returns>The operation result.</returns>
    public static JsonArray? GetJsonArrayOrNull(IDictionary<string, object?> arguments, string name)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            JsonArray jsonArray => jsonArray,
            JsonNode jsonNode => jsonNode as JsonArray,
            JsonElement element when element.ValueKind == JsonValueKind.Array => JsonNode.Parse(element.GetRawText()) as JsonArray,
            string text when TryParseJsonNode(text, out var node) => node as JsonArray,
            IEnumerable<object?> sequence => JsonSerializer.SerializeToNode(sequence) as JsonArray,
            _ => JsonSerializer.SerializeToNode(value) as JsonArray
        };
    }

    /// <summary>
    /// Gets string dictionary.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="name">The name.</param>
    /// <returns>The operation result.</returns>
    public static Dictionary<string, string> GetStringDictionary(IDictionary<string, object?> arguments, string name)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return [];
        }

        if (value is IDictionary<string, string> textMap)
        {
            return new Dictionary<string, string>(textMap, StringComparer.OrdinalIgnoreCase);
        }

        if (value is IDictionary<string, object?> objectMap)
        {
            return objectMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        }

        JsonObject? jsonObject = value switch
        {
            JsonObject obj => obj,
            JsonElement element when element.ValueKind == JsonValueKind.Object => JsonNode.Parse(element.GetRawText()) as JsonObject,
            string text when TryParseJsonNode(text, out var node) => node as JsonObject,
            _ => JsonSerializer.SerializeToNode(value) as JsonObject
        };

        if (jsonObject is null)
        {
            return [];
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in jsonObject)
        {
            result[property.Key] = property.Value?.ToString() ?? string.Empty;
        }

        return result;
    }

    /// <summary>
    /// Gets object dictionary.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="name">The name.</param>
    /// <returns>The operation result.</returns>
    public static Dictionary<string, object?> GetObjectDictionary(IDictionary<string, object?> arguments, string name)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!arguments.TryGetValue(name, out var value) || value is null)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        if (value is Dictionary<string, object?> dictionary)
        {
            return new Dictionary<string, object?>(dictionary, StringComparer.Ordinal);
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            return readOnlyDictionary.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }

        if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.TryGetInt64(out var int64Value)
                        ? int64Value
                        : property.Value.TryGetDouble(out var doubleValue)
                            ? doubleValue
                            : property.Value.GetRawText(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => property.Value.GetRawText()
                };
            }

            return result;
        }

        throw new ArgumentException($"Argument '{name}' must be an object map.");
    }

    private static bool TryGetJsonInt32(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt32(out value);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        value = default;
        return false;
    }

    private static bool TryConvertToInt32(object value, out int convertedValue)
    {
        try
        {
            convertedValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            convertedValue = default;
            return false;
        }
        catch (InvalidCastException)
        {
            convertedValue = default;
            return false;
        }
        catch (OverflowException)
        {
            convertedValue = default;
            return false;
        }
    }

    private static bool TryParseJsonNode(string text, out JsonNode? node)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            node = null;
            return false;
        }

        try
        {
            node = JsonNode.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            node = null;
            return false;
        }
    }
}

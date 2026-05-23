using System.Globalization;
using System.Text.Json;

namespace LeanKernel.Tools.BuiltIn;

internal static class ToolArgumentReader
{
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
}

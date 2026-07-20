using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using LeanKernel.Channels.Common.Configuration;

using Microsoft.Extensions.Options;

namespace LeanKernel.Channels.Signal;

public sealed class GatewayChannelClient(HttpClient httpClient, IOptions<GatewaySettings> settings)
{
    public async Task<GatewayTurnResult> RunTurnAsync(object input, string bearerToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = settings.Value.Model,
            input,
            agent = new
            {
                name = settings.Value.AgentName
            }
        }), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return new GatewayTurnResult($"Gateway request failed: {(int)response.StatusCode}", []);
        }

        var payload = await response.Content.ReadAsStringAsync(ct);
        return ExtractResponseText(payload);
    }

    private static GatewayTurnResult ExtractResponseText(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new GatewayTurnResult(string.Empty, []);
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.TryGetProperty("output", out var output)
                && output.ValueKind == JsonValueKind.Array)
            {
                var builder = new StringBuilder();
                var styles = new List<SignalTextStyle>();

                foreach (var outputItem in output.EnumerateArray())
                {
                    if (!outputItem.TryGetProperty("content", out var content)
                        || content.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var contentItem in content.EnumerateArray())
                    {
                        if (!contentItem.TryGetProperty("type", out var typeElement)
                            || !string.Equals(typeElement.GetString(), "output_text", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var text = contentItem.TryGetProperty("text", out var textElement)
                            ? textElement.GetString()
                            : null;

                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        var explicitStyles = ParseTextStyles(contentItem, text.Length);
                        var renderedText = text;
                        IReadOnlyList<SignalTextStyle> segmentStyles = explicitStyles;

                        if (segmentStyles.Count == 0)
                        {
                            var markdownFallback = ParseMarkdownTextStyles(text);
                            renderedText = markdownFallback.Text;
                            segmentStyles = markdownFallback.TextStyles;
                        }

                        var offset = builder.Length;
                        if (builder.Length > 0)
                        {
                            builder.AppendLine();
                        }

                        offset = builder.Length;
                        builder.Append(renderedText);

                        foreach (var segmentStyle in segmentStyles)
                        {
                            styles.Add(new SignalTextStyle
                            {
                                Start = offset + segmentStyle.Start,
                                Length = segmentStyle.Length,
                                Style = segmentStyle.Style
                            });
                        }
                    }
                }

                if (builder.Length > 0)
                {
                    return new GatewayTurnResult(builder.ToString(), styles);
                }
            }
        }
        catch (JsonException)
        {
        }

        return new GatewayTurnResult(payload, []);
    }

    private static IReadOnlyList<SignalTextStyle> ParseTextStyles(JsonElement contentItem, int segmentLength)
    {
        var mappedStyles = new List<SignalTextStyle>();

        if (contentItem.TryGetProperty("textStyles", out var textStylesElement)
            && textStylesElement.ValueKind == JsonValueKind.Array)
        {
            AddMappedStyles(mappedStyles, textStylesElement, segmentLength);
        }

        if (contentItem.TryGetProperty("annotations", out var annotationsElement)
            && annotationsElement.ValueKind == JsonValueKind.Array)
        {
            AddMappedStyles(mappedStyles, annotationsElement, segmentLength);
        }

        return mappedStyles;
    }

    private static void AddMappedStyles(List<SignalTextStyle> mappedStyles, JsonElement styleArray, int segmentLength)
    {
        foreach (var styleItem in styleArray.EnumerateArray())
        {
            if (!TryMapSignalStyle(styleItem, out var style)
                || !TryReadRange(styleItem, out var start, out var length))
            {
                continue;
            }

            if (start < 0 || length <= 0 || start >= segmentLength)
            {
                continue;
            }

            var boundedLength = Math.Min(length, segmentLength - start);
            if (boundedLength <= 0)
            {
                continue;
            }

            mappedStyles.Add(new SignalTextStyle
            {
                Start = start,
                Length = boundedLength,
                Style = style
            });
        }
    }

    private static MarkdownTextResult ParseMarkdownTextStyles(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new MarkdownTextResult(text, []);
        }

        var output = new StringBuilder();
        var styles = new List<SignalTextStyle>();
        var stack = new Stack<OpenStyle>();

        for (var i = 0; i < text.Length;)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                output.Append(text[i + 1]);
                i += 2;
                continue;
            }

            if (TryMatchDelimiter(text, i, out var delimiter, out var style))
            {
                if (stack.Count > 0 && string.Equals(stack.Peek().Delimiter, delimiter, StringComparison.Ordinal))
                {
                    var open = stack.Pop();
                    var length = output.Length - open.Start;
                    if (length > 0)
                    {
                        styles.Add(new SignalTextStyle
                        {
                            Start = open.Start,
                            Length = length,
                            Style = open.Style
                        });
                    }

                    i += delimiter.Length;
                    continue;
                }

                if (HasClosingDelimiter(text, i + delimiter.Length, delimiter))
                {
                    stack.Push(new OpenStyle(delimiter, style, output.Length));
                    i += delimiter.Length;
                    continue;
                }
            }

            output.Append(text[i]);
            i++;
        }

        return new MarkdownTextResult(output.ToString(), styles);
    }

    private static bool TryMatchDelimiter(string text, int index, out string delimiter, out string style)
    {
        delimiter = string.Empty;
        style = string.Empty;

        if (index + 1 < text.Length)
        {
            var pair = text.AsSpan(index, 2);
            if (pair.SequenceEqual("**"))
            {
                delimiter = "**";
                style = "BOLD";
                return true;
            }

            if (pair.SequenceEqual("~~"))
            {
                delimiter = "~~";
                style = "STRIKETHROUGH";
                return true;
            }

            if (pair.SequenceEqual("||"))
            {
                delimiter = "||";
                style = "SPOILER";
                return true;
            }
        }

        if (text[index] == '`')
        {
            delimiter = "`";
            style = "MONOSPACE";
            return true;
        }

        if (text[index] == '*')
        {
            delimiter = "*";
            style = "ITALIC";
            return true;
        }

        if (text[index] == '_')
        {
            delimiter = "_";
            style = "ITALIC";
            return true;
        }

        return false;
    }

    private static bool HasClosingDelimiter(string text, int searchFrom, string delimiter)
    {
        if (searchFrom >= text.Length)
        {
            return false;
        }

        return text.IndexOf(delimiter, searchFrom, StringComparison.Ordinal) >= 0;
    }

    private static bool TryMapSignalStyle(JsonElement styleItem, out string style)
    {
        style = string.Empty;
        var rawStyle = styleItem.TryGetProperty("style", out var styleElement)
            ? styleElement.GetString()
            : styleItem.TryGetProperty("textStyle", out var textStyleElement)
                ? textStyleElement.GetString()
                : null;

        if (string.IsNullOrWhiteSpace(rawStyle))
        {
            return false;
        }

        style = rawStyle.Trim().ToUpperInvariant() switch
        {
            "BOLD" => "BOLD",
            "ITALIC" => "ITALIC",
            "SPOILER" => "SPOILER",
            "STRIKETHROUGH" => "STRIKETHROUGH",
            "STRIKE-THROUGH" => "STRIKETHROUGH",
            "STRIKE_THROUGH" => "STRIKETHROUGH",
            "MONOSPACE" => "MONOSPACE",
            "CODE" => "MONOSPACE",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(style);
    }

    private static bool TryReadRange(JsonElement styleItem, out int start, out int length)
    {
        start = 0;
        length = 0;

        var hasStart = styleItem.TryGetProperty("start", out var startElement)
            || styleItem.TryGetProperty("offset", out startElement);

        if (!hasStart
            || !startElement.TryGetInt32(out start))
        {
            return false;
        }

        if (styleItem.TryGetProperty("length", out var lengthElement)
            && lengthElement.TryGetInt32(out length))
        {
            return true;
        }

        if (styleItem.TryGetProperty("end", out var endElement)
            && endElement.TryGetInt32(out var end)
            && end > start)
        {
            length = end - start;
            return true;
        }

        if (styleItem.TryGetProperty("stop", out var stopElement)
            && stopElement.TryGetInt32(out var stop)
            && stop > start)
        {
            length = stop - start;
            return true;
        }

        return false;
    }
}

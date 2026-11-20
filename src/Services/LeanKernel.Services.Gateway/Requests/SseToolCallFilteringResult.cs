using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LeanKernel.Services.Gateway.Requests;

/// <summary>
/// Copies upstream responses and removes server-side tool handoffs from SSE events.
/// </summary>
internal sealed class SseToolCallFilteringResult(
    HttpResponseMessage upstream,
    CancellationTokenSource timeout,
    string internalPath,
    ILogger<HttpContext> logger) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        using (upstream)
        using (timeout)
        {
            var state = new EventStreamState();
            try
            {
                httpContext.Response.StatusCode = (int)upstream.StatusCode;
                httpContext.Response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? Constants.ContentTypes.Json;
                await using var source = await upstream.Content.ReadAsStreamAsync(timeout.Token);
                if (!upstream.IsSuccessStatusCode || !string.Equals(upstream.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                {
                    await source.CopyToAsync(httpContext.Response.Body, timeout.Token);
                    return;
                }

                await CopyEventsAsync(source, httpContext.Response.Body, state, timeout.Token);
            }
            catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpRequestException or IOException)
            {
                if (httpContext.Response.HasStarted || state.WroteResponse)
                {
                    logger.LogWarning(ex, "Chat completions proxy stream failed after the response started.");
                    httpContext.Abort();
                    throw;
                }

                var failure = IEndpointRouteBuilderExtensions.MapProxyFailure(internalPath, ex, logger);
                await failure.ExecuteAsync(httpContext);
            }
        }
    }

    private static async Task CopyEventsAsync(Stream source, Stream destination, EventStreamState state, CancellationToken ct)
    {
        using var reader = new StreamReader(source);
        await using var writer = new StreamWriter(destination, new UTF8Encoding(false), leaveOpen: true);
        var lines = new List<string>();
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            if (line.Length != 0)
            {
                lines.Add(line);
                continue;
            }

            var output = state.ProcessEvent(lines);
            lines.Clear();
            if (output is not null)
            {
                await writer.WriteAsync(output.AsMemory(), ct);
                await writer.FlushAsync(ct);
                state.WroteResponse = true;
            }

            if (state.IsDone)
            {
                var discard = new char[4096];
                while (await reader.ReadAsync(discard.AsMemory(), ct) != 0)
                {
                    // Let the upstream finish without forwarding events after its terminal marker.
                }

                return;
            }
        }

        // An unfinished SSE event is never delivered, including a partial tool call.
        if (lines.Count == 0 && state.CanComplete)
        {
            await writer.WriteAsync("data: [DONE]\n\n".AsMemory(), ct);
            await writer.FlushAsync(ct);
        }
    }

    private sealed class EventStreamState
    {
        private readonly Dictionary<int, bool> _choices = [];
        private bool _error;
        private bool _done;

        public bool WroteResponse { get; set; }

        public bool IsDone => _done;

        public bool CanComplete => !_done && !_error && _choices.Count > 0 && _choices.Values.All(static finished => finished);

        public string? ProcessEvent(List<string> lines)
        {
            if (_done)
            {
                return null;
            }

            var payload = string.Join("\n", lines.Where(IsDataLine).Select(GetData));
            if (payload == "[DONE]")
            {
                _done = true;
                return FormatEvent(lines);
            }

            if (lines.Any(static line => line == "event:error" || line == "event: error"))
            {
                _error = true;
            }

            if (lines.Any(IsDataLine))
            {
                ProcessPayload(payload, lines);
            }

            return FormatEvent(lines);
        }

        private static bool IsDataLine(string line) => line == "data" || line.StartsWith("data:", StringComparison.Ordinal);

        private static string GetData(string line)
        {
            if (line.Length <= 5)
            {
                return string.Empty;
            }

            return line[5] == ' ' ? line[6..] : line[5..];
        }

        private static string FormatEvent(List<string> lines) => string.Join("\n", lines) + "\n\n";

        private void ProcessPayload(string payload, List<string> lines)
        {
            try
            {
                if (JsonNode.Parse(payload) is not JsonObject json)
                {
                    _error = true;
                    return;
                }

                _error |= json["error"] is not null;
                if (json["choices"] is not JsonArray choices)
                {
                    _error |= json.ContainsKey("choices") || json["usage"] is null;
                    return;
                }

                var changed = false;
                foreach (var item in choices)
                {
                    if (item is JsonObject choice)
                    {
                        changed |= ProcessChoice(choice);
                    }
                    else
                    {
                        _error = true;
                    }
                }

                if (changed)
                {
                    var first = lines.FindIndex(IsDataLine);
                    lines.RemoveAll(IsDataLine);
                    lines.Insert(first, "data: " + json.ToJsonString());
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                _error = true;
            }
        }

        private bool ProcessChoice(JsonObject choice)
        {
            if (choice["delta"] is not null && choice["delta"] is not JsonObject)
            {
                _error = true;
            }

            var delta = choice["delta"] as JsonObject;
            var hasDelta = delta is { Count: > 0 };
            var changed = delta?.Remove("tool_calls") ?? false;
            changed |= delta?.Remove("function_call") ?? false;

            if (choice["index"] is not JsonValue indexValue || !indexValue.TryGetValue<int>(out var index) || index < 0)
            {
                _error = true;
                return changed;
            }

            _choices.TryAdd(index, false);
            if (hasDelta || choice["finish_reason"] is null)
            {
                _choices[index] = false;
            }

            if (choice["finish_reason"] is not null)
            {
                if (choice["finish_reason"] is not JsonValue finishValue || !finishValue.TryGetValue<string>(out var finish))
                {
                    _error = true;
                }
                else if (finish is "tool_calls" or "function_call")
                {
                    choice["finish_reason"] = null;
                    _choices[index] = false;
                    changed = true;
                }
                else if (finish is "stop" or "length" or "content_filter")
                {
                    _choices[index] = true;
                }
                else
                {
                    _error = true;
                }
            }

            return changed;
        }
    }
}

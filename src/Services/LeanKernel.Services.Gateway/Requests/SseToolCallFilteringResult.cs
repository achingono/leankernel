namespace LeanKernel.Services.Gateway.Requests;

/// <summary>
/// Filters SSE <c>data:</c> lines containing <c>tool_calls</c> from the proxied Chat Completions stream.
/// The MAF <c>MapOpenAIChatCompletions</c> handler converts intermediate <c>FunctionCallContent</c>
/// chunks into SSE <c>tool_calls</c>, which leak raw function calls to clients that cannot execute them.
/// This strips those events at the transport layer so clients receive only final text content while
/// tools are still executed server-side by the agent pipeline.
/// </summary>
internal sealed class SseToolCallFilteringResult(Stream source, string contentType) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.ContentType = contentType;
        httpContext.Response.StatusCode = 200;

        using var reader = new StreamReader(source);
        await using var writer = new StreamWriter(httpContext.Response.Body, leaveOpen: true);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal) &&
                line.Contains("tool_calls", StringComparison.Ordinal))
            {
                continue;
            }

            await writer.WriteLineAsync(line);
        }

        await writer.FlushAsync();
    }
}
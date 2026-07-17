using System.Diagnostics;
using LeanKernel.Logic.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.Telemetry;

/// <summary>
/// Decorator around <see cref="IChatClient"/> that captures model/provider/usage/cost telemetry
/// from each model invocation and stores it in <see cref="ITurnTelemetryCollector"/>.
/// </summary>
internal sealed class TelemetryCapturingChatClient(
    IChatClient inner,
    ITurnTelemetryCollector collector,
    CostEstimateTable costTable,
    IOptions<TelemetrySettings> settings,
    ILogger<TelemetryCapturingChatClient>? logger = null) : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var requestedModel = options?.ModelId;
        var sw = Stopwatch.StartNew();
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        sw.Stop();

        CaptureTelemetry(response, requestedModel, sw.Elapsed);

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestedModel = options?.ModelId;
        var sw = Stopwatch.StartNew();
        ChatResponse? finalResponse = null;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (string.IsNullOrEmpty(update.Text) && finalResponse is not null)
            {
                yield return update;
                continue;
            }

            if (finalResponse is null)
            {
                finalResponse = new ChatResponse(new List<ChatMessage>
                {
                    new(update.Role ?? ChatRole.Assistant, update.Text ?? string.Empty)
                    {
                        AuthorName = update.AuthorName,
                    }
                });
                finalResponse.ModelId = update.ModelId;
            }
            else if (!string.IsNullOrEmpty(update.Text))
            {
                finalResponse.Messages.Add(new ChatMessage(update.Role ?? ChatRole.Assistant, update.Text)
                {
                    AuthorName = update.AuthorName,
                });
            }

            yield return update;
        }

        sw.Stop();

        if (finalResponse is not null)
            CaptureTelemetry(finalResponse, requestedModel, sw.Elapsed);
    }

    private void CaptureTelemetry(ChatResponse response, string? requestedModel, TimeSpan latency)
    {
        try
        {
            var usage = response.Usage;
            var model = response.ModelId;

            var telemetry = new TurnTelemetry
            {
                RequestedModel = requestedModel,
                ServedModel = model,
                Provider = ExtractProvider(model),
                ModelId = model,
                ApiBase = TryGetAdditionalProperty(response.AdditionalProperties, "api_base"),
                PromptTokens = (int?)usage?.InputTokenCount,
                CompletionTokens = (int?)usage?.OutputTokenCount,
                TotalTokens = (int?)usage?.TotalTokenCount,
                Currency = settings.Value.Currency,
                Latency = latency,
                CapturedAt = DateTimeOffset.UtcNow
            };

            // Try to read cost from response additional properties (LiteLLM header passthrough)
            var reportedCost = TryExtractCost(response.AdditionalProperties);
            if (reportedCost.HasValue)
            {
                telemetry.ResponseCost = reportedCost.Value;
                telemetry.CostIsEstimated = false;
            }
            else if (settings.Value.UseCostEstimate
                     && telemetry.PromptTokens.HasValue
                     && telemetry.CompletionTokens.HasValue)
            {
                var estimatedCost = costTable.Estimate(model, telemetry.PromptTokens.Value, telemetry.CompletionTokens.Value);
                if (estimatedCost.HasValue)
                {
                    telemetry.ResponseCost = estimatedCost.Value;
                    telemetry.CostIsEstimated = true;
                }
            }

            collector.Capture(telemetry);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to capture telemetry for model {Model}", response.ModelId);
        }
    }

    private static decimal? TryExtractCost(AdditionalPropertiesDictionary? properties)
    {
        if (properties is null)
            return null;

        if (properties.TryGetValue("x-litellm-response-cost", out var costObj)
            && costObj is string costStr
            && decimal.TryParse(costStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var cost))
        {
            return cost;
        }

        return null;
    }

    private static string? TryGetAdditionalProperty(AdditionalPropertiesDictionary? properties, string key)
    {
        if (properties is not null && properties.TryGetValue(key, out var value))
            return value?.ToString();
        return null;
    }

    private static string? ExtractProvider(string? model)
    {
        if (string.IsNullOrEmpty(model))
            return null;

        return model.ToLowerInvariant() switch
        {
            var m when m.StartsWith("gpt-") => "openai",
            var m when m.StartsWith("o1") => "openai",
            var m when m.StartsWith("claude-") => "anthropic",
            var m when m.StartsWith("gemini-") => "google",
            var m when m.StartsWith("llama-") || m.StartsWith("meta-llama") => "groq",
            var m when m.Contains("mistral") => "mistral",
            var m when m.Contains("deepseek") => "deepseek",
            _ => null
        };
    }
}

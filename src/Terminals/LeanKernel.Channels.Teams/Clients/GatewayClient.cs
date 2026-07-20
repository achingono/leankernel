using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using LeanKernel.Channels.Common.Configuration;

using Microsoft.Extensions.Options;

namespace LeanKernel.Channels.Teams.Clients;

/// <summary>HTTP client that forwards Teams turns to the LeanKernel gateway.</summary>
public sealed class GatewayClient(HttpClient httpClient, IOptions<GatewaySettings> settings)
{
    /// <summary>Sends user input to the gateway and returns the response text.</summary>
    /// <param name="input">The user input text.</param>
    /// <param name="bearerToken">The bearer token for gateway authentication.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The gateway response text.</returns>
    public async Task<string> RunTurnAsync(string input, string bearerToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
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
            return $"Gateway request failed: {(int)response.StatusCode}";
        }

        var payload = await response.Content.ReadAsStringAsync(ct);
        return ExtractResponseText(payload);
    }

    private static string ExtractResponseText(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.TryGetProperty("output", out var output)
                && output.ValueKind == JsonValueKind.Array)
            {
                var builder = new StringBuilder();

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

                        if (builder.Length > 0)
                        {
                            builder.AppendLine();
                        }

                        builder.Append(text);
                    }
                }

                if (builder.Length > 0)
                {
                    return builder.ToString();
                }
            }
        }
        catch (JsonException)
        {
            // Fallback to raw payload for non-JSON responses.
        }

        return payload;
    }
}
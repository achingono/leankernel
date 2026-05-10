using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Wiki;

/// <summary>
/// Semantic extraction using LiteLLM's OpenAI-compatible chat completions API.
/// Exposes both awaited extraction for the self-improvement pipeline and a compatibility wrapper for callers that do not await results.
/// </summary>
public sealed class LlmWikiExtractor
{
    private const int MaxExchangeCharacters = 4_000;

    private static readonly string[] ChatCompletionPaths = ["/v1/chat/completions", "/chat/completions"];

    private readonly HttpClient _liteLlmClient;
    private readonly IWikiStore _wiki;
    private readonly ILogger<LlmWikiExtractor> _logger;
    private readonly string _model;
    private readonly double _temperature;

    private const string ExtractionInstructions = """
        Extract factual claims from this conversation exchange as structured 5W1H facts.
        Return ONLY a valid JSON array. Each item has: dimension (who/what/when/where/why/how), subject, claims (string array).

        Return empty array [] if no facts are present.
        """;

    /// <summary>
    /// Represents the llm wiki extractor.
    /// </summary>
    public LlmWikiExtractor(
        HttpClient liteLlmClient,
        IWikiStore wiki,
        IOptions<LeanKernelConfig> config,
        ILogger<LlmWikiExtractor> logger)
    {
        _liteLlmClient = liteLlmClient;
        _wiki = wiki;
        _logger = logger;
        _model = config.Value.LiteLlm.DefaultModel;
        _temperature = config.Value.Ollama.Temperature;
    }

    /// <summary>
    /// Starts extraction in the background for compatibility with callers that cannot await pipeline work.
    /// Prefer <see cref="ExtractAndIngestAsync" /> for new learning pipeline code.
    /// </summary>
    /// <param name="userMessage">The user message from the completed turn.</param>
    /// <param name="assistantResponse">The assistant response from the completed turn.</param>
    /// <param name="sourceId">The source identifier used when persisting facts.</param>
    public void ExtractAsync(string userMessage, string assistantResponse, string sourceId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await ExtractAndIngestAsync(userMessage, assistantResponse, sourceId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Async LLM extraction failed for source {SourceId}", sourceId);
            }
        });
    }

    /// <summary>
    /// Extracts semantic facts from an exchange and ingests them into the wiki.
    /// </summary>
    /// <param name="userMessage">The user message from the completed turn.</param>
    /// <param name="assistantResponse">The assistant response from the completed turn.</param>
    /// <param name="sourceId">The source identifier used when persisting facts.</param>
    /// <param name="ct">A token used to cancel extraction.</param>
    public async Task ExtractAndIngestAsync(string userMessage, string assistantResponse, string sourceId, CancellationToken ct)
    {
        var response = await CallLiteLlmAsync(userMessage, assistantResponse, ct);

        if (string.IsNullOrWhiteSpace(response))
        {
            _logger.LogDebug("LiteLLM returned empty response for extraction");
            return;
        }

        var entries = ParseExtractedFacts(response, sourceId);
        if (entries.Count == 0)
        {
            _logger.LogDebug("No facts parsed from LiteLLM response");
            return;
        }

        await _wiki.IngestFactsAsync(entries, ct);
        _logger.LogDebug("LLM extraction: ingested {Count} entries from {SourceId}", entries.Count, sourceId);
    }

    private async Task<string> CallLiteLlmAsync(string userMessage, string assistantResponse, CancellationToken ct)
    {
        var exchange = BuildExchange(userMessage, assistantResponse);
        var payload = JsonSerializer.Serialize(new
        {
            model = _model,
            temperature = _temperature,
            messages = new object[]
            {
                new { role = "system", content = ExtractionInstructions },
                new { role = "user", content = exchange }
            }
        });

        HttpResponseMessage? notFoundResponse = null;

        foreach (var path in ChatCompletionPaths)
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await _liteLlmClient.PostAsync(path, content, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                notFoundResponse = response;
                continue;
            }

            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;
        }

        notFoundResponse?.EnsureSuccessStatusCode();
        return string.Empty;
    }

    private static string BuildExchange(string userMessage, string assistantResponse)
    {
        var trimmedUserMessage = TrimForExtraction(userMessage);
        var trimmedAssistantResponse = TrimForExtraction(assistantResponse);

        return $"USER:\n{trimmedUserMessage}\n\nASSISTANT:\n{trimmedAssistantResponse}";
    }

    private static string TrimForExtraction(string text)
    {
        text = text.Trim();
        if (text.Length <= MaxExchangeCharacters)
            return text;

        return text[..MaxExchangeCharacters] + "\n...[truncated for extraction]";
    }

    private static List<WikiEntry> ParseExtractedFacts(string jsonResponse, string sourceId)
    {
        var entries = new List<WikiEntry>();

        try
        {
            var jsonStart = jsonResponse.IndexOf('[');
            var jsonEnd = jsonResponse.LastIndexOf(']');
            if (jsonStart < 0 || jsonEnd < 0) return entries;

            var jsonStr = jsonResponse[jsonStart..(jsonEnd + 1)];
            using var doc = JsonDocument.Parse(jsonStr);

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var dimensionStr = item.GetProperty("dimension").GetString() ?? "what";
                var subject = item.GetProperty("subject").GetString() ?? "Unknown";
                var claims = item.GetProperty("claims").EnumerateArray()
                    .Select(c => c.GetString() ?? string.Empty)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToList();

                if (claims.Count == 0) continue;

                if (!Enum.TryParse<WikiDimension>(dimensionStr, ignoreCase: true, out var dimension))
                    dimension = WikiDimension.What;

                var id = $"llm-{Slugify(subject)}-{DateTimeOffset.UtcNow:yyyyMMdd}";
                entries.Add(new WikiEntry
                {
                    Id = id,
                    Dimension = dimension,
                    Subject = subject,
                    Facts = claims.Select(claim => new WikiFact
                    {
                        Claim = claim,
                        Confidence = 0.85,
                        Source = sourceId,
                        LastConfirmed = DateTimeOffset.UtcNow,
                        EstimatedTokens = (int)Math.Ceiling(claim.Length / 4.0)
                    }).ToList()
                });
            }
        }
        catch (JsonException)
        {
        }

        return entries;
    }

    private static string Slugify(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            text.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-").Trim('-');
}

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Archivist.Wiki;

/// <summary>
/// Semantic extraction using local Ollama LLM.
/// Runs asynchronously to avoid turn latency.
/// </summary>
public sealed class LlmWikiExtractor
{
    private readonly HttpClient _ollamaClient;
    private readonly IWikiStore _wiki;
    private readonly ILogger<LlmWikiExtractor> _logger;
    private readonly string _model;
    private readonly double _temperature;

    private const string ExtractionPrompt = """
        Extract factual claims from this conversation exchange as structured 5W1H facts.
        Return ONLY a valid JSON array. Each item has: dimension (who/what/when/where/why/how), subject, claims (string array).
        
        Return empty array [] if no facts found.
        
        Exchange:
        USER: {0}
        ASSISTANT: {1}
        
        JSON:
        """;

    public LlmWikiExtractor(
        HttpClient ollamaClient,
        IWikiStore wiki,
        IOptions<LeanKernelConfig> config,
        ILogger<LlmWikiExtractor> logger)
    {
        _ollamaClient = ollamaClient;
        _wiki = wiki;
        _logger = logger;
        _model = config.Value.Ollama.Model;
        _temperature = config.Value.Ollama.Temperature;
    }

    /// <summary>
    /// Fire-and-forget async extraction. Does not await or return result.
    /// Failures are logged but don't propagate.
    /// </summary>
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

    private async Task ExtractAndIngestAsync(string userMessage, string assistantResponse, string sourceId, CancellationToken ct)
    {
        var prompt = string.Format(ExtractionPrompt, userMessage, assistantResponse);
        var response = await CallOllamaAsync(prompt, ct);

        if (string.IsNullOrWhiteSpace(response))
        {
            _logger.LogDebug("Ollama returned empty response for extraction");
            return;
        }

        var entries = ParseExtractedFacts(response, sourceId);
        if (entries.Count == 0)
        {
            _logger.LogDebug("No facts parsed from Ollama response");
            return;
        }

        await _wiki.IngestFactsAsync(entries, ct);
        _logger.LogDebug("LLM extraction: ingested {Count} entries from {SourceId}", entries.Count, sourceId);
    }

    private async Task<string> CallOllamaAsync(string prompt, CancellationToken ct)
    {
        var request = new
        {
            model = _model,
            prompt = prompt,
            stream = false,
            temperature = _temperature
        };

        var content = new StringContent(
            JsonSerializer.Serialize(request),
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await _ollamaClient.PostAsync("/api/generate", content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.GetProperty("response").GetString() ?? "";
    }

    private static List<WikiEntry> ParseExtractedFacts(string jsonResponse, string sourceId)
    {
        var entries = new List<WikiEntry>();

        try
        {
            // Try to extract JSON array from response (Ollama may include commentary)
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
                    .Select(c => c.GetString() ?? "")
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
                        Confidence = 0.85, // LLM extraction gets higher confidence than regex
                        Source = sourceId,
                        LastConfirmed = DateTimeOffset.UtcNow,
                        EstimatedTokens = (int)Math.Ceiling(claim.Length / 4.0)
                    }).ToList()
                });
            }
        }
        catch (JsonException ex)
        {
            // Log but don't throw; graceful degradation
        }

        return entries;
    }

    private static string Slugify(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            text.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-").Trim('-');
}

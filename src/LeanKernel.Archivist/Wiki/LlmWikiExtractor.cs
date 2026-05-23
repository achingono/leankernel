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
public sealed class LlmWikiExtractor : IWikiFactExtractor
{
    private const int MaxExchangeCharacters = 4_000;

    private static readonly string[] ChatCompletionPaths = ["/v1/chat/completions", "/chat/completions"];

    private readonly HttpClient _liteLlmClient;
    private readonly IWikiStore _wiki;
    private readonly WikiFactMapper _mapper;
    private readonly ILogger<LlmWikiExtractor> _logger;
    private readonly string _model;
    private readonly double _temperature;

    private const string ExtractionInstructions = """
        Extract grounded facts from this conversation exchange as structured 5W1H records.
        Return ONLY a valid JSON object with this shape:
        {
          "facts": [
            {
              "who": "...",
              "what": "...",
              "when": "...",
              "where": "...",
              "why": "...",
              "how": "...",
              "claim": "...",
              "subject": "...",
              "primaryDimension": "who|what|when|where|why|how",
              "sourceQuote": "...",
              "summaryHint": "...",
              "aliases": ["..."],
              "tags": ["..."]
            }
          ]
        }
        If no valid fact exists, return {"facts":[]}.
        """;

    private static readonly HashSet<string> GenericSubjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "unknown", "action", "information", "document", "file", "files", "item", "thing"
    };

    /// <summary>
    /// Represents the llm wiki extractor.
    /// </summary>
    public LlmWikiExtractor(
        HttpClient liteLlmClient,
        IWikiStore wiki,
        WikiFactMapper mapper,
        IOptions<LeanKernelConfig> config,
        ILogger<LlmWikiExtractor> logger)
    {
        _liteLlmClient = liteLlmClient;
        _wiki = wiki;
        _mapper = mapper;
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
        var extractedFacts = await ExtractAsync(userMessage, assistantResponse, sourceId, ct);
        var entries = _mapper.Map(extractedFacts, sourceId);
        if (entries.Count == 0)
        {
            _logger.LogDebug("No valid mapped facts from source {SourceId}", sourceId);
            return;
        }

        await _wiki.IngestFactsAsync(entries, ct);
        _logger.LogDebug("LLM extraction: ingested {Count} entries from {SourceId}", entries.Count, sourceId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExtractedWikiFact>> ExtractAsync(
        string userMessage,
        string assistantResponse,
        string sourceId,
        CancellationToken ct)
    {
        var response = await CallLiteLlmAsync(userMessage, assistantResponse, ct);
        if (string.IsNullOrWhiteSpace(response))
        {
            _logger.LogDebug("LiteLLM returned empty response for extraction source {SourceId}", sourceId);
            return [];
        }

        return ParseExtractedFacts(response, sourceId, _logger);
    }

    private async Task<string> CallLiteLlmAsync(string userMessage, string assistantResponse, CancellationToken ct)
    {
        var exchange = BuildExchange(userMessage, assistantResponse);
        var payload = JsonSerializer.Serialize(new
        {
            model = _model,
            temperature = _temperature,
            response_format = new
            {
                type = "json_object"
            },
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

    internal static IReadOnlyList<ExtractedWikiFact> ParseExtractedFacts(
        string jsonResponse,
        string sourceId,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(jsonResponse))
            return [];

        var normalized = NormalizeJsonBlock(jsonResponse);

        try
        {
            var response = JsonSerializer.Deserialize(normalized, LeanKernelJsonContext.Default.WikiExtractionResponse);
            if (response?.Facts is null || response.Facts.Count == 0)
                return [];

            var validFacts = new List<ExtractedWikiFact>(response.Facts.Count);
            foreach (var fact in response.Facts)
            {
                var validationFailure = ValidateFact(fact);
                if (validationFailure is not null)
                {
                    logger.LogWarning(
                        "LLM extraction rejected fact for source {SourceId}: {Reason}",
                        sourceId,
                        validationFailure);
                    continue;
                }

                validFacts.Add(fact);
            }

            return validFacts;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "LLM extraction parse failure for source {SourceId}", sourceId);
            return [];
        }
    }

    private static string? ValidateFact(ExtractedWikiFact fact)
    {
        if (!Enum.TryParse<WikiDimension>(fact.PrimaryDimension, ignoreCase: true, out _))
            return $"invalid primaryDimension '{fact.PrimaryDimension}'";
        if (string.IsNullOrWhiteSpace(fact.Claim))
            return "blank claim";
        if (string.IsNullOrWhiteSpace(fact.Subject))
            return "blank subject";
        if (GenericSubjects.Contains(fact.Subject.Trim()))
            return $"generic subject '{fact.Subject}'";

        var hasContext =
            !string.IsNullOrWhiteSpace(fact.Who) ||
            !string.IsNullOrWhiteSpace(fact.What) ||
            !string.IsNullOrWhiteSpace(fact.When) ||
            !string.IsNullOrWhiteSpace(fact.Where) ||
            !string.IsNullOrWhiteSpace(fact.Why) ||
            !string.IsNullOrWhiteSpace(fact.How);

        if (!hasContext)
            return "no 5W1H context populated";
        if (string.IsNullOrWhiteSpace(fact.SourceQuote) && fact.Claim.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 4)
            return "ungrounded short claim";

        return null;
    }

    private static string NormalizeJsonBlock(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = trimmed.Split('\n');
            if (lines.Length >= 3)
            {
                var contentLines = lines.Skip(1).Take(lines.Length - 2);
                return string.Join('\n', contentLines).Trim();
            }
        }

        var objectStart = trimmed.IndexOf('{');
        var objectEnd = trimmed.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd >= objectStart)
            return trimmed[objectStart..(objectEnd + 1)];

        return trimmed;
    }
}

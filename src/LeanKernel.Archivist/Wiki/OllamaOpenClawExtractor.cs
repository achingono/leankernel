using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;

namespace LeanKernel.Archivist.Wiki;

/// <summary>
/// Extracts facts from OpenClaw wiki pages using Ollama via HTTP API.
/// Supports session log corroboration and optional Qdrant semantic retrieval.
/// </summary>
public sealed class OllamaOpenClawExtractor
{
    private readonly HttpClient _httpClient;
    private readonly OllamaConfig _config;
    private readonly ILogger<OllamaOpenClawExtractor> _logger;

    private static readonly HashSet<string> GenericSubjects = new(StringComparer.OrdinalIgnoreCase)
    {
        "unknown", "action", "information", "document", "file", "files", "item", "thing", "fact", "claim", "statement"
    };

    public OllamaOpenClawExtractor(
        HttpClient httpClient,
        IOptions<LeanKernelConfig> config,
        ILogger<OllamaOpenClawExtractor> logger)
    {
        _httpClient = httpClient;
        _config = config.Value.Ollama;
        _logger = logger;
    }

    /// <summary>
    /// Extracts facts from an OpenClaw wiki page.
    /// </summary>
    public async Task<List<OllamaExtractedFact>> ExtractFromPageAsync(
        string pageTitle,
        string pageContent,
        Dictionary<string, string> sections,
        List<string> sessionLogSnippets,
        CancellationToken ct)
    {
        var facts = new List<OllamaExtractedFact>();
        if (string.IsNullOrWhiteSpace(pageContent))
            return facts;

        var prompt = BuildExtractionPrompt(pageTitle, pageContent, sections, sessionLogSnippets);
        var response = await CallOllamaAsync(prompt, ct);

        if (string.IsNullOrWhiteSpace(response))
        {
            _logger.LogDebug("Ollama returned empty response for page {PageTitle}", pageTitle);
            return facts;
        }

        facts.AddRange(ParseExtractedFacts(response, pageTitle));
        return facts;
    }

    /// <summary>
    /// Queries Qdrant for document-sourced facts related to a claim (fallback when session logs don't corroborate).
    /// </summary>
    public async Task<List<string>> QueryQdrantForCorroborationAsync(
        string claim,
        CancellationToken ct)
    {
        // TODO: Implement Qdrant search for document sources when IQdrantClient is available
        // For now, return empty list; this is a fallback mechanism
        return await Task.FromResult(new List<string>());
    }

    private string BuildExtractionPrompt(
        string pageTitle,
        string pageContent,
        Dictionary<string, string> sections,
        List<string> sessionLogSnippets)
    {
        var sectionsText = new StringBuilder();
        foreach (var (dim, content) in sections)
        {
            if (!string.IsNullOrWhiteSpace(content))
            {
                var truncated = content.Length > 500 ? content[..500] + "..." : content;
                sectionsText.AppendLine($"## {dim}");
                sectionsText.AppendLine(truncated);
                sectionsText.AppendLine();
            }
        }

        var sessionContext = sessionLogSnippets.Count > 0
            ? $"\n\n### Session Log Context:\n{string.Join("\n---\n", sessionLogSnippets.Take(5))}"
            : "";

        var contentTruncated = pageContent[..Math.Min(2000, pageContent.Length)];
        
        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("Extract grounded facts from this OpenClaw wiki page. Return ONLY valid JSON.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine($"Page Title: {pageTitle}");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Content:");
        promptBuilder.AppendLine(contentTruncated);
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Sections:");
        promptBuilder.Append(sectionsText);
        promptBuilder.AppendLine(sessionContext);
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Return a JSON object with this exact structure:");
        promptBuilder.AppendLine(@"{");
        promptBuilder.AppendLine(@"  ""facts"": [");
        promptBuilder.AppendLine(@"    {");
        promptBuilder.AppendLine(@"      ""entryId"": ""who-name|what-concept|when-date|etc"",");
        promptBuilder.AppendLine(@"      ""dimension"": ""who|what|when|where|why|how"",");
        promptBuilder.AppendLine(@"      ""subject"": ""entity name"",");
        promptBuilder.AppendLine(@"      ""claim"": ""the fact statement"",");
        promptBuilder.AppendLine(@"      ""confidence"": 0.65,");
        promptBuilder.AppendLine(@"      ""corroboration_source"": ""session_log|qdrant|wiki_only"",");
        promptBuilder.AppendLine(@"      ""context"": {");
        promptBuilder.AppendLine(@"        ""who"": ""..."",");
        promptBuilder.AppendLine(@"        ""what"": ""..."",");
        promptBuilder.AppendLine(@"        ""when"": ""..."",");
        promptBuilder.AppendLine(@"        ""where"": ""..."",");
        promptBuilder.AppendLine(@"        ""why"": ""..."",");
        promptBuilder.AppendLine(@"        ""how"": ""...""");
        promptBuilder.AppendLine(@"      },");
        promptBuilder.AppendLine(@"      ""tags"": [""tag1"", ""tag2""],");
        promptBuilder.AppendLine(@"      ""source_quote"": ""direct quote from page or session""");
        promptBuilder.AppendLine(@"    }");
        promptBuilder.AppendLine(@"  ]");
        promptBuilder.AppendLine(@"}");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Rules:");
        promptBuilder.AppendLine("- Confidence: 0.85+ for corroborated claims, 0.65-0.84 for wiki-only");
        promptBuilder.AppendLine("- Each fact must have a clear subject and claim");
         promptBuilder.AppendLine("- Tags should include domain and dimension");
         promptBuilder.AppendLine("- Only extract valid, specific facts");
         promptBuilder.AppendLine(@"- If no valid facts exist, return {""facts"":[]}");
        
         return promptBuilder.ToString();
    }

    private async Task<string> CallOllamaAsync(string prompt, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = _config.Model,
            prompt = prompt,
            temperature = _config.Temperature,
            stream = false
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_config.TimeoutSeconds));

        try
        {
            var response = await _httpClient.PostAsync($"{_config.BaseUrl}/api/generate", content, cts.Token);
            response.EnsureSuccessStatusCode();
            var responseText = await response.Content.ReadAsStringAsync(cts.Token);

            // Parse Ollama response
            using var doc = JsonDocument.Parse(responseText);
            return doc.RootElement.GetProperty("response").GetString() ?? "";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Ollama extraction request failed");
            return "";
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Ollama extraction timed out after {TimeoutSeconds}s", _config.TimeoutSeconds);
            return "";
        }
    }

    private List<OllamaExtractedFact> ParseExtractedFacts(string jsonResponse, string pageTitle)
    {
        var facts = new List<OllamaExtractedFact>();
        if (string.IsNullOrWhiteSpace(jsonResponse))
            return facts;

        var normalized = NormalizeJsonBlock(jsonResponse);

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            var root = doc.RootElement;

            if (!root.TryGetProperty("facts", out var factsArray))
                return facts;

            foreach (var factElement in factsArray.EnumerateArray())
            {
                try
                {
                    var fact = new OllamaExtractedFact
                    {
                        EntryId = factElement.GetProperty("entryId").GetString() ?? "",
                        Dimension = factElement.GetProperty("dimension").GetString() ?? "",
                        Subject = factElement.GetProperty("subject").GetString() ?? "",
                        Claim = factElement.GetProperty("claim").GetString() ?? "",
                        Confidence = factElement.GetProperty("confidence").GetDouble(),
                        CorroborationSource = factElement.GetProperty("corroboration_source").GetString() ?? "wiki_only",
                        SourceQuote = factElement.TryGetProperty("source_quote", out var sq) ? sq.GetString() : null
                    };

                    var contextObj = factElement.GetProperty("context");
                    fact.Context = new()
                    {
                        Who = contextObj.TryGetProperty("who", out var w) ? w.GetString() : null,
                        What = contextObj.TryGetProperty("what", out var wh) ? wh.GetString() : null,
                        When = contextObj.TryGetProperty("when", out var wn) ? wn.GetString() : null,
                        Where = contextObj.TryGetProperty("where", out var wr) ? wr.GetString() : null,
                        Why = contextObj.TryGetProperty("why", out var wy) ? wy.GetString() : null,
                        How = contextObj.TryGetProperty("how", out var h) ? h.GetString() : null
                    };

                    if (factElement.TryGetProperty("tags", out var tagsArray))
                    {
                        foreach (var tag in tagsArray.EnumerateArray())
                        {
                            if (tag.GetString() is string t)
                                fact.Tags.Add(t);
                        }
                    }

                    var validation = ValidateFact(fact);
                    if (validation is null)
                    {
                        facts.Add(fact);
                    }
                    else
                    {
                        _logger.LogDebug("Ollama fact rejected for {PageTitle}: {Reason}", pageTitle, validation);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse individual Ollama fact for {PageTitle}", pageTitle);
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Ollama extraction JSON parse failure");
        }

        return facts;
    }

    private static string? ValidateFact(OllamaExtractedFact fact)
    {
        if (!Enum.TryParse<WikiDimension>(fact.Dimension, ignoreCase: true, out _))
            return $"invalid dimension '{fact.Dimension}'";
        if (string.IsNullOrWhiteSpace(fact.Claim))
            return "blank claim";
        if (string.IsNullOrWhiteSpace(fact.Subject))
            return "blank subject";
        if (GenericSubjects.Contains(fact.Subject.Trim()))
            return $"generic subject '{fact.Subject}'";
        if (fact.Confidence < 0.0 || fact.Confidence > 1.0)
            return $"invalid confidence {fact.Confidence}";

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

/// <summary>
/// Represents a fact extracted by Ollama from an OpenClaw wiki page.
/// </summary>
public sealed class OllamaExtractedFact
{
    public string EntryId { get; set; } = "";
    public string Dimension { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Claim { get; set; } = "";
    public double Confidence { get; set; } = 0.65;
    public string CorroborationSource { get; set; } = "wiki_only";
    public string? SourceQuote { get; set; }
    public OllamaFactContext Context { get; set; } = new();
    public List<string> Tags { get; set; } = [];
}

/// <summary>
/// 5W1H context for extracted facts.
/// </summary>
public sealed class OllamaFactContext
{
    public string? Who { get; set; }
    public string? What { get; set; }
    public string? When { get; set; }
    public string? Where { get; set; }
    public string? Why { get; set; }
    public string? How { get; set; }
}

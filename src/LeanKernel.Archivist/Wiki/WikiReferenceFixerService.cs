// Script to fix invalid wiki references using LLM
// Run from: dotnet run --project src/LeanKernel.Host -- --tool=fix-references
//
// This tool:
// 1. Fetches all wiki entries
// 2. Identifies facts with invalid OpenClaw-style references (../domain/page.md)
// 3. Uses Ollama to suggest corrections
// 4. Updates facts with corrected references

using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Archivist.Wiki;

/// <summary>
/// Fixes invalid wiki references by using LLM to suggest corrections.
/// </summary>
public sealed class WikiReferenceFixerService
{
    private readonly IWikiStore _wikiStore;
    private readonly IHttpClientFactory _httpFactory;
    private readonly LeanKernelConfig _config;
    private readonly ILogger<WikiReferenceFixerService> _logger;
    
    private static readonly Regex ReferencePattern = new(@"\.\./[\w-]+/[\w-]+\.md");
    
    // Map OpenClaw domains to LeanKernel dimensions
    private static readonly Dictionary<string, string> DomainToDimension = new(StringComparer.OrdinalIgnoreCase)
    {
        { "career", "what" },
        { "context", "where" },
        { "financial", "how" },
        { "identity", "who" },
        { "relationships", "who" },
        { "ventures", "what" },
        { "wisdom", "why" },
    };

    public WikiReferenceFixerService(
        IWikiStore wikiStore,
        IHttpClientFactory httpFactory,
        LeanKernelConfig config,
        ILogger<WikiReferenceFixerService> logger)
    {
        _wikiStore = wikiStore;
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Scans all wiki entries and fixes invalid references.
    /// </summary>
    public async Task<WikiReferenceFixResult> FixInvalidReferencesAsync(
        bool dryRun = true,
        CancellationToken ct = default)
    {
        var result = new WikiReferenceFixResult();
        
        _logger.LogInformation("🔍 Scanning for invalid references...");
        
        // Fetch all entries
        var query = new WikiQuery { PageSize = 1000 };
        var entries = await _wikiStore.QueryAsync(query, ct);
        
        var entryUpdates = new List<(WikiEntry entry, List<FactFix> fixes)>();
        
        foreach (var entry in entries)
        {
            var fixes = new List<FactFix>();
            var updatedFacts = new List<WikiFact>();
            
            foreach (var fact in entry.Facts)
            {
                var factFixes = await FindAndFixReferencesInFactAsync(fact, entry, ct);
                
                if (factFixes.Any())
                {
                    fixes.AddRange(factFixes);
                    result.FactsWithReferences++;
                    
                    // Apply fixes
                    var fixedClaim = ApplyFixes(fact.Claim, factFixes);
                    var fixedContext = ApplyFixesToContext(fact.Context, factFixes);
                    
                    updatedFacts.Add(fact with 
                    { 
                        Claim = fixedClaim,
                        Context = fixedContext
                    });
                }
                else
                {
                    updatedFacts.Add(fact);
                }
            }
            
            if (fixes.Any())
            {
                entryUpdates.Add((entry with { Facts = updatedFacts }, fixes));
            }
        }
        
        result.EntriesWithReferences = entryUpdates.Count;
        result.TotalReferencesFound = entryUpdates.Sum(e => e.fixes.Count);
        
        _logger.LogInformation($"Found {result.EntriesWithReferences} entries with {result.TotalReferencesFound} invalid references");
        
        if (!dryRun && entryUpdates.Any())
        {
            _logger.LogInformation($"Applying fixes to {entryUpdates.Count} entries...");
            
            foreach (var (entry, fixes) in entryUpdates)
            {
                await _wikiStore.UpsertAsync(entry, ct);
                _logger.LogInformation($"✓ Fixed {fixes.Count} references in {entry.Subject}");
                result.FixesApplied += fixes.Count;
            }
        }
        
        return result;
    }

    private async Task<List<FactFix>> FindAndFixReferencesInFactAsync(
        WikiFact fact,
        WikiEntry entry,
        CancellationToken ct)
    {
        var fixes = new List<FactFix>();
        
        // Collect all text to search
        var textsToSearch = new List<string> { fact.Claim };
        if (fact.Context != null)
        {
            if (!string.IsNullOrEmpty(fact.Context.Who)) textsToSearch.Add(fact.Context.Who);
            if (!string.IsNullOrEmpty(fact.Context.What)) textsToSearch.Add(fact.Context.What);
            if (!string.IsNullOrEmpty(fact.Context.When)) textsToSearch.Add(fact.Context.When);
            if (!string.IsNullOrEmpty(fact.Context.Where)) textsToSearch.Add(fact.Context.Where);
            if (!string.IsNullOrEmpty(fact.Context.Why)) textsToSearch.Add(fact.Context.Why);
            if (!string.IsNullOrEmpty(fact.Context.How)) textsToSearch.Add(fact.Context.How);
        }
        
        var allText = string.Join("\n", textsToSearch);
        var matches = ReferencePattern.Matches(allText);
        
        var processedRefs = new HashSet<string>();
        foreach (Match match in matches)
        {
            var reference = match.Value;
            if (processedRefs.Contains(reference))
                continue;
                
            processedRefs.Add(reference);
            
            // Extract domain and page
            var parts = reference.Split("/");
            if (parts.Length != 2)
                continue;
                
            var domain = parts[0].Replace("../", "");
            var page = parts[1].Replace(".md", "");
            
            // Normalize
            var normalized = NormalizeReference(domain, page);
            
            // Use LLM to verify the fix is appropriate
            var fixSuggestion = await GetLLMFixSuggestionAsync(reference, fact.Claim, entry.Subject, ct);
            
            if (!string.IsNullOrEmpty(fixSuggestion) && fixSuggestion != "REMOVE")
            {
                fixes.Add(new FactFix 
                { 
                    Original = reference,
                    Suggested = fixSuggestion,
                    Context = fact.Claim[..Math.Min(100, fact.Claim.Length)]
                });
            }
        }
        
        return fixes;
    }

    private string NormalizeReference(string domain, string page)
    {
        if (!DomainToDimension.TryGetValue(domain, out var dimension))
            dimension = "what"; // Default
            
        return $"`{dimension}-{page}`";
    }

    private async Task<string?> GetLLMFixSuggestionAsync(
        string reference,
        string claimContext,
        string entrySubject,
        CancellationToken ct)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            
            var prompt = $"""
Given this wiki reference that needs to be fixed:
Reference: {reference}
Context: {claimContext[..Math.Min(200, claimContext.Length)]}
Entry: {entrySubject}

Convert this OpenClaw-style path to a LeanKernel wiki entry reference.
For example:
- ../career/narrative.md -> `what-narrative`
- ../identity/personality.md -> `who-personality`
- ../context/tensions.md -> `where-tensions`

Reply with ONLY the corrected reference in backticks (e.g., `dimension-topic`) or "REMOVE" if it should be deleted.
""";

            var request = new
            {
                model = "neural-chat",
                prompt = prompt,
                stream = false,
                temperature = 0.2
            };
            
            var response = await client.PostAsJsonAsync(
                "http://localhost:11434/api/generate",
                request,
                ct);
                
            if (!response.IsSuccessStatusCode)
                return null;
                
            var json = await response.Content.ReadAsAsync<JsonElement>(ct);
            var suggestion = json.GetProperty("response").GetString()?.Trim();
            
            return suggestion;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"LLM error for {reference}: {ex.Message}");
            return null;
        }
    }

    private string ApplyFixes(string text, List<FactFix> fixes)
    {
        var result = text;
        foreach (var fix in fixes)
        {
            result = result.Replace(fix.Original, fix.Suggested);
        }
        return result;
    }

    private WikiFactContext? ApplyFixesToContext(WikiFactContext? context, List<FactFix> fixes)
    {
        if (context == null)
            return null;
            
        return context with
        {
            Who = ApplyFixes(context.Who ?? "", fixes),
            What = ApplyFixes(context.What ?? "", fixes),
            When = ApplyFixes(context.When ?? "", fixes),
            Where = ApplyFixes(context.Where ?? "", fixes),
            Why = ApplyFixes(context.Why ?? "", fixes),
            How = ApplyFixes(context.How ?? "", fixes),
        };
    }
}

public sealed record FactFix
{
    public required string Original { get; init; }
    public required string Suggested { get; init; }
    public string Context { get; init; } = "";
}

public sealed class WikiReferenceFixResult
{
    public int EntriesWithReferences { get; set; }
    public int FactsWithReferences { get; set; }
    public int TotalReferencesFound { get; set; }
    public int FixesApplied { get; set; }
}

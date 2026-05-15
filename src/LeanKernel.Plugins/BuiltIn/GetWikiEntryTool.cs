using System.Text;
using System.Text.RegularExpressions;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Plugins.Sdk;

namespace LeanKernel.Plugins.BuiltIn;

/// <summary>
/// Deterministic wiki entry lookup backed by <see cref="IWikiStore"/>.
/// </summary>
[ToolMetadata(
    Name = "get_wiki_entry",
    Description = "Fetch a specific structured wiki entry by entryId, or by dimension + subject. Use this for exact wiki lookup after discovery.",
    Category = ToolCategory.Wiki)]
public sealed class GetWikiEntryTool : ITool
{
    private static readonly Regex SlugRegex = new(@"[^a-z0-9]+", RegexOptions.Compiled);
    private readonly IWikiStore _wiki;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetWikiEntryTool"/> class.
    /// </summary>
    public GetWikiEntryTool(IWikiStore wiki)
    {
        _wiki = wiki;
    }

    /// <inheritdoc />
    public string Name => "get_wiki_entry";

    /// <inheritdoc />
    public string Description =>
        "Get an exact wiki entry by canonical id, or by dimension + subject when you need structured 5W1H facts instead of vector chunks.";

    /// <inheritdoc />
    public string Category => ToolCategory.Wiki.ToString().ToLowerInvariant();

    /// <inheritdoc />
    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "entryId": { "type": "string", "description": "Canonical wiki entry id (e.g., who-john-doe)." },
            "dimension": { "type": "string", "enum": ["who","what","where","when","why","how"] },
            "subject": { "type": "string", "description": "Subject value used with dimension when entryId is not provided." }
          }
        }
        """;

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(string parametersJson, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (!TryParseParameters(parametersJson, out var entryId, out var dimension, out var subject, out var error))
            {
                return new ToolResult
                {
                    ToolName = Name,
                    Success = false,
                    Error = error,
                    Duration = sw.Elapsed
                };
            }

            WikiEntry? entry;
            if (!string.IsNullOrWhiteSpace(entryId))
            {
                entry = await _wiki.GetAsync(entryId, ct);
            }
            else
            {
                var dimensionValue = dimension!.Value;
                var normalizedSubject = (subject ?? string.Empty).Trim();
                var canonicalEntryId = $"{dimensionValue.ToString().ToLowerInvariant()}-{Slugify(normalizedSubject)}";

                entry = await _wiki.GetAsync(canonicalEntryId, ct);
                if (entry is null)
                {
                    var dimensionEntries = await _wiki.ListByDimensionAsync(dimensionValue, ct);
                    entry = dimensionEntries.FirstOrDefault(e =>
                        string.Equals(e.Subject.Trim(), normalizedSubject, StringComparison.OrdinalIgnoreCase) ||
                        e.Aliases.Any(alias => string.Equals(alias.Trim(), normalizedSubject, StringComparison.OrdinalIgnoreCase)));
                }
            }

            return new ToolResult
            {
                ToolName = Name,
                Success = true,
                Output = entry is null
                    ? "No wiki entry found for the requested key."
                    : FormatEntry(entry),
                Duration = sw.Elapsed
            };
        }
        catch (Exception ex)
        {
            return new ToolResult
            {
                ToolName = Name,
                Success = false,
                Error = ex.Message,
                Duration = sw.Elapsed
            };
        }
    }

    private static bool TryParseParameters(
        string parametersJson,
        out string? entryId,
        out WikiDimension? dimension,
        out string? subject,
        out string error)
    {
        entryId = null;
        dimension = null;
        subject = null;
        error = string.Empty;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(parametersJson);
            var root = document.RootElement;

            if (root.TryGetProperty("entryId", out var entryIdElement))
            {
                entryId = entryIdElement.GetString();
            }

            if (!string.IsNullOrWhiteSpace(entryId))
            {
                return true;
            }

            if (!root.TryGetProperty("dimension", out var dimensionElement) ||
                !Enum.TryParse<WikiDimension>(dimensionElement.GetString(), true, out var parsedDimension))
            {
                error = "Parameter 'dimension' is required and must be one of: who, what, where, when, why, how.";
                return false;
            }

            if (!root.TryGetProperty("subject", out var subjectElement))
            {
                error = "Parameter 'subject' is required when 'entryId' is not provided.";
                return false;
            }

            subject = subjectElement.GetString();
            if (string.IsNullOrWhiteSpace(subject))
            {
                error = "Parameter 'subject' cannot be blank when 'entryId' is not provided.";
                return false;
            }

            dimension = parsedDimension;
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            error = "Invalid parameters JSON. Expected {\"entryId\":\"...\"} or {\"dimension\":\"who\",\"subject\":\"...\"}.";
            return false;
        }
    }

    private static string FormatEntry(WikiEntry entry)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {entry.Subject} (id: {entry.Id}, dimension: {entry.Dimension.ToString().ToLowerInvariant()})");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(entry.Summary))
        {
            sb.AppendLine(entry.Summary);
            sb.AppendLine();
        }

        sb.AppendLine("## Facts");
        if (entry.Facts.Count == 0)
        {
            sb.AppendLine("- none");
        }
        else
        {
            foreach (var fact in entry.Facts)
            {
                var sourcePart = string.IsNullOrWhiteSpace(fact.Source) ? string.Empty : $", source: {fact.Source}";
                sb.AppendLine($"- {fact.Claim} (confidence: {fact.Confidence:F2}{sourcePart})");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Aliases");
        sb.AppendLine(entry.Aliases.Count == 0 ? "none" : string.Join(", ", entry.Aliases));
        sb.AppendLine();
        sb.AppendLine("## Tags");
        sb.AppendLine(entry.Tags.Count == 0 ? "none" : string.Join(", ", entry.Tags));
        return sb.ToString().TrimEnd();
    }

    private static string Slugify(string text)
    {
        var normalized = text.ToLowerInvariant().Trim();
        return SlugRegex.Replace(normalized, "-").Trim('-');
    }
}

using System.Text.Json;

namespace LeanKernel.Logic.Memory;

/// <summary>
/// Repairs missing 5W1H fields using the small reasoning model when enough context is available.
/// </summary>
public sealed class MemoryFieldRepairService
{
    private readonly IReasoningModel _reasoningModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryFieldRepairService"/> class.
    /// </summary>
    /// <param name="reasoningModel">The reasoning model used for field repair.</param>
    public MemoryFieldRepairService(IReasoningModel reasoningModel)
    {
        _reasoningModel = reasoningModel;
    }

    /// <summary>
    /// Attempts to repair missing 5W1H fields for a memory page.
    /// </summary>
    /// <param name="snapshot">The memory page being repaired.</param>
    /// <param name="currentFields">The current field values.</param>
    /// <param name="missingFields">The field names that are missing.</param>
    /// <param name="relatedPages">Related pages that can be used as evidence.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A dictionary containing repaired fields.</returns>
    public async Task<IReadOnlyDictionary<string, string>> TryRepairMissingFieldsAsync(
        MemoryPageSnapshot snapshot,
        IReadOnlyDictionary<string, string?> currentFields,
        IReadOnlyList<string> missingFields,
        IReadOnlyList<MemoryPageSnapshot> relatedPages,
        CancellationToken cancellationToken)
    {
        if (!_reasoningModel.Enabled || missingFields.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var prompt = BuildPrompt(snapshot, currentFields, missingFields, relatedPages);
        var content = await _reasoningModel.CompleteAsync(
            "Repair only missing 5W1H fields. Return strict JSON object with keys Who,What,When,Where,Why,How.",
            prompt,
            512,
            cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content) || !TryExtractJsonObject(content!, out var json))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in missingFields)
            {
                if (!MemoryPageFields.FiveWOneHSet.Contains(field)
                    || !doc.RootElement.TryGetProperty(field, out var el)
                    || el.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = el.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result[field] = value.Trim();
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Builds the model prompt used to repair missing fields.
    /// </summary>
    /// <param name="snapshot">The memory page being repaired.</param>
    /// <param name="currentFields">The current field values.</param>
    /// <param name="missingFields">The missing field names.</param>
    /// <param name="relatedPages">The related evidence pages.</param>
    /// <returns>The repair prompt.</returns>
    private static string BuildPrompt(
        MemoryPageSnapshot snapshot,
        IReadOnlyDictionary<string, string?> currentFields,
        IReadOnlyList<string> missingFields,
        IReadOnlyList<MemoryPageSnapshot> relatedPages)
    {
        var related = relatedPages
            .Take(6)
            .Select(page => new RelatedEvidencePage(
                page.Key,
                page.FactText.Length <= 320 ? page.FactText : page.FactText[..320],
                ["related"],
                0,
                0.0))
            .ToList();

        return $"""
Current fields JSON: {JsonSerializer.Serialize(currentFields, SmallModelJson.Options)}
Missing fields JSON: {JsonSerializer.Serialize(missingFields, SmallModelJson.Options)}
Related evidence JSON: {JsonSerializer.Serialize(related, SmallModelJson.Options)}

Page content:
{snapshot.Content}
""";
    }

    /// <summary>
    /// Tries to extract the outermost JSON object from model output.
    /// </summary>
    /// <param name="content">The model output to inspect.</param>
    /// <param name="json">When this method returns, contains the extracted JSON object.</param>
    /// <returns><c>true</c> when a JSON object was extracted; otherwise, <c>false</c>.</returns>
    private static bool TryExtractJsonObject(string content, out string json)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            json = content[start..(end + 1)];
            return true;
        }

        json = string.Empty;
        return false;
    }
}

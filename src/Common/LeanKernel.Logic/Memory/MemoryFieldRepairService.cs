using System.Text.Json;

namespace LeanKernel.Logic.Memory;

public sealed class MemoryFieldRepairService
{
    private readonly IReasoningModel _reasoningModel;

    public MemoryFieldRepairService(IReasoningModel reasoningModel)
    {
        _reasoningModel = reasoningModel;
    }

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

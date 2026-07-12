using System.Diagnostics.Metrics;
using System.Text.Json;

namespace LeanKernel.Logic.Memory;

public sealed class MemoryDimensionClassifier
{
    private static readonly Meter Meter = new("LeanKernel.Logic.Memory", "1.0.0");
    private static readonly Counter<long> SourceCounter = Meter.CreateCounter<long>("memory.dimensions.source");

    private readonly IReasoningModel _reasoningModel;

    public MemoryDimensionClassifier(IReasoningModel reasoningModel)
    {
        _reasoningModel = reasoningModel;
    }

    public async Task<DimensionClassificationResult> ClassifyAsync(
        MemoryPageSnapshot snapshot,
        IReadOnlyDictionary<string, string?> fields,
        IReadOnlyList<string> missingFields,
        IReadOnlyList<MemoryPageSnapshot> related,
        CancellationToken cancellationToken)
    {
        var deterministicScores = ScoreDeterministic(fields, snapshot);
        var primary = deterministicScores
            .OrderByDescending(static x => x.Score)
            .ThenBy(static x => x.Dimension, StringComparer.Ordinal)
            .FirstOrDefault()?.Dimension ?? "what";

        var secondary = deterministicScores
            .Where(score => score.Score > 0 && !score.Dimension.Equals(primary, StringComparison.Ordinal))
            .OrderByDescending(static x => x.Score)
            .Select(static x => x.Dimension)
            .ToList();

        var method = "deterministic";
        var ambiguous = deterministicScores.Count(score => score.Score > 0) <= 1
            || deterministicScores.Select(score => score.Score).Distinct().Count() <= 2
            || missingFields.Count >= 3;

        if (_reasoningModel.Enabled && ambiguous)
        {
            var request = new DimensionExtractionRequest(
                snapshot.FactText,
                fields,
                missingFields,
                related.Take(6).Select(ToEvidence).ToList());

            var response = await TryGetModelResponseAsync(request, cancellationToken).ConfigureAwait(false);
            if (response is not null && !string.IsNullOrWhiteSpace(response.PrimaryDimension))
            {
                primary = MemoryPageFields.NormalizeDimension(response.PrimaryDimension);
                secondary = response.SecondaryDimensions
                    .Select(MemoryPageFields.NormalizeDimension)
                    .Where(d => !d.Equals(primary, StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                method = "hybrid-llm";
                deterministicScores = deterministicScores
                    .Select(score => score with
                    {
                        Source = secondary.Contains(score.Dimension, StringComparer.Ordinal)
                            || score.Dimension.Equals(primary, StringComparison.Ordinal)
                            ? "llm-refined"
                            : score.Source
                    })
                    .ToList();
                SourceCounter.Add(1, new KeyValuePair<string, object?>("source", "llm-refined"));
            }
            else
            {
                SourceCounter.Add(1, new KeyValuePair<string, object?>("source", "fallback"));
            }
        }
        else
        {
            SourceCounter.Add(1, new KeyValuePair<string, object?>("source", "deterministic"));
        }

        if (string.IsNullOrWhiteSpace(primary))
        {
            primary = "what";
        }

        return new DimensionClassificationResult(primary, secondary, deterministicScores, method);
    }

    private async Task<DimensionExtractionResponse?> TryGetModelResponseAsync(
        DimensionExtractionRequest request,
        CancellationToken cancellationToken)
    {
        var systemPrompt = "Extract and rank memory dimensions. Return strict JSON only.";
        var userPrompt = JsonSerializer.Serialize(request, SmallModelJson.Options);
        var result = await _reasoningModel.CompleteAsync(systemPrompt, userPrompt, 512, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(result))
        {
            return null;
        }

        if (!TryExtractJsonObject(result!, out var json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DimensionExtractionResponse>(json, SmallModelJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<MemoryDimensionScore> ScoreDeterministic(
        IReadOnlyDictionary<string, string?> fields,
        MemoryPageSnapshot snapshot)
    {
        var scores = new List<MemoryDimensionScore>();
        foreach (var field in MemoryPageFields.FiveWOneH)
        {
            var value = fields.GetValueOrDefault(field);
            var score = BaseScore(field, value);
            var reasons = new List<string>();
            if (!string.IsNullOrWhiteSpace(value))
            {
                reasons.Add("populated");
                if (value!.Length > 20)
                {
                    score += 8;
                    reasons.Add("specific");
                }

                if (snapshot.FactText.Contains(value, StringComparison.OrdinalIgnoreCase))
                {
                    score += 6;
                    reasons.Add("in-fact");
                }

                if (IsVague(value))
                {
                    score -= 20;
                    reasons.Add("vague");
                }
            }

            scores.Add(new MemoryDimensionScore(
                field.ToLowerInvariant(),
                Math.Max(0, score),
                reasons.Count == 0 ? "missing" : string.Join(",", reasons)));
        }

        return scores;
    }

    private static int BaseScore(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        return field switch
        {
            "What" => 100,
            "Who" => 80,
            "Where" => 70,
            "When" => 60,
            "Why" => 50,
            "How" => 50,
            _ => 0
        };
    }

    private static bool IsVague(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "someone" or "recently" or "somewhere" or "for some reason" or "somehow";
    }

    private static RelatedEvidencePage ToEvidence(MemoryPageSnapshot snapshot)
    {
        var snippet = snapshot.FactText.Length <= 320
            ? snapshot.FactText
            : snapshot.FactText[..320];
        return new RelatedEvidencePage(snapshot.Key, snippet, ["related"], 1, 0.0);
    }

    private static bool TryExtractJsonObject(string content, out string json)
    {
        var trimmed = content.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            json = trimmed[start..(end + 1)];
            return true;
        }

        json = string.Empty;
        return false;
    }
}

public sealed record DimensionClassificationResult(
    string PrimaryDimension,
    IReadOnlyList<string> SecondaryDimensions,
    IReadOnlyList<MemoryDimensionScore> DimensionScores,
    string Source);

using System.Diagnostics.Metrics;
using System.Text.Json;

namespace LeanKernel.Logic.Memory;

/// <summary>
/// Classifies memory pages into primary and secondary 5W1H dimensions.
/// </summary>
public sealed class MemoryDimensionClassifier
{
    private static readonly Meter Meter = new("LeanKernel.Logic.Memory", "1.0.0");
    private static readonly Counter<long> SourceCounter = Meter.CreateCounter<long>("memory.dimensions.source");

    private readonly IReasoningModel _reasoningModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryDimensionClassifier"/> class.
    /// </summary>
    /// <param name="reasoningModel">The reasoning model used to refine ambiguous classifications.</param>
    public MemoryDimensionClassifier(IReasoningModel reasoningModel)
    {
        _reasoningModel = reasoningModel;
    }

    /// <summary>
    /// Classifies the dimensions of a memory page using deterministic scoring and optional model refinement.
    /// </summary>
    /// <param name="snapshot">The page snapshot being classified.</param>
    /// <param name="fields">The parsed 5W1H field values.</param>
    /// <param name="missingFields">The fields that are missing.</param>
    /// <param name="related">Related pages that can be used as evidence.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The classification result.</returns>
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

    /// <summary>
    /// Tries to obtain a refined dimension classification from the reasoning model.
    /// </summary>
    /// <param name="request">The model request payload.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The parsed model response, or <c>null</c> when no valid response is available.</returns>
    private async Task<DimensionExtractionResponse?> TryGetModelResponseAsync(
        DimensionExtractionRequest request,
        CancellationToken cancellationToken)
    {
        var systemPrompt = "Extract and rank memory dimensions. Return strict JSON only.";
        var userPrompt = JsonSerializer.Serialize(request, ModelResponseJson.Options);
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
            return JsonSerializer.Deserialize<DimensionExtractionResponse>(json, ModelResponseJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Scores each 5W1H dimension using deterministic heuristics.
    /// </summary>
    /// <param name="fields">The parsed 5W1H field values.</param>
    /// <param name="snapshot">The page snapshot being classified.</param>
    /// <returns>The computed dimension scores.</returns>
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

    /// <summary>
    /// Computes the base score for a populated field.
    /// </summary>
    /// <param name="field">The field name being scored.</param>
    /// <param name="value">The field value.</param>
    /// <returns>The base score for the field.</returns>
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

    /// <summary>
    /// Determines whether a field value is too vague to strongly support a dimension.
    /// </summary>
    /// <param name="value">The field value to inspect.</param>
    /// <returns><c>true</c> when the value is considered vague; otherwise, <c>false</c>.</returns>
    private static bool IsVague(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "someone" or "recently" or "somewhere" or "for some reason" or "somehow";
    }

    /// <summary>
    /// Converts a page snapshot into a compact evidence payload for model prompts.
    /// </summary>
    /// <param name="snapshot">The page snapshot to convert.</param>
    /// <returns>The related evidence payload.</returns>
    private static RelatedEvidencePage ToEvidence(MemoryPageSnapshot snapshot)
    {
        var snippet = snapshot.FactText.Length <= 320
            ? snapshot.FactText
            : snapshot.FactText[..320];
        return new RelatedEvidencePage(snapshot.Key, snippet, ["related"], 1, 0.0);
    }

    /// <summary>
    /// Tries to extract the outermost JSON object from model output.
    /// </summary>
    /// <param name="content">The model output to inspect.</param>
    /// <param name="json">When this method returns, contains the extracted JSON object.</param>
    /// <returns><c>true</c> when a JSON object was extracted; otherwise, <c>false</c>.</returns>
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

/// <summary>
/// Represents the outcome of classifying a memory page into 5W1H dimensions.
/// </summary>
public sealed record DimensionClassificationResult(
    string PrimaryDimension,
    IReadOnlyList<string> SecondaryDimensions,
    IReadOnlyList<MemoryDimensionScore> DimensionScores,
    string Source);
using System.Diagnostics.Metrics;

namespace LeanKernel.Logic.Memory;

public sealed class MemoryPageNormalizer
{
    private static readonly Meter Meter = new("LeanKernel.Logic.Memory", "1.0.0");
    private static readonly Counter<long> NormalizedCounter = Meter.CreateCounter<long>("memory.pages.normalized");
    private static readonly Counter<long> PartialCounter = Meter.CreateCounter<long>("memory.pages.partial");

    private readonly MemoryDimensionClassifier _dimensionClassifier;
    private readonly MemoryPageLinker _linker;
    private readonly MemoryGraphReasoner _graphReasoner;
    private readonly MemoryFieldRepairService _repairService;
    private readonly MemoryPageRenderer _renderer;
    private readonly MemoryPageKeyBuilder _keyBuilder;

    public MemoryPageNormalizer(
        MemoryDimensionClassifier dimensionClassifier,
        MemoryPageLinker linker,
        MemoryGraphReasoner graphReasoner,
        MemoryFieldRepairService repairService,
        MemoryPageRenderer renderer,
        MemoryPageKeyBuilder keyBuilder)
    {
        _dimensionClassifier = dimensionClassifier;
        _linker = linker;
        _graphReasoner = graphReasoner;
        _repairService = repairService;
        _renderer = renderer;
        _keyBuilder = keyBuilder;
    }

    public async Task<MemoryPageNormalizationResult> NormalizeAsync(
        MemoryPageSnapshot snapshot,
        IReadOnlyList<MemoryPageSnapshot> relatedPages,
        bool enableRepair,
        CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Who"] = snapshot.Fields.GetValueOrDefault("Who"),
            ["What"] = !string.IsNullOrWhiteSpace(snapshot.FactText)
                ? snapshot.FactText
                : snapshot.Fields.GetValueOrDefault("What"),
            ["When"] = snapshot.Fields.GetValueOrDefault("When")
                ?? snapshot.Metadata.GetValueOrDefault("RecordedAt")
                ?? (snapshot.EffectiveTimestamp == default ? null : snapshot.EffectiveTimestamp.ToString("O")),
            ["Where"] = snapshot.Fields.GetValueOrDefault("Where"),
            ["Why"] = snapshot.Fields.GetValueOrDefault("Why"),
            ["How"] = snapshot.Fields.GetValueOrDefault("How")
        };

        var missing = MissingFields(fields);

        if (enableRepair && missing.Count > 0)
        {
            var repaired = await _repairService.TryRepairMissingFieldsAsync(snapshot, fields, missing, relatedPages, cancellationToken)
                .ConfigureAwait(false);
            foreach (var entry in repaired)
            {
                if (string.IsNullOrWhiteSpace(fields[entry.Key]))
                {
                    fields[entry.Key] = entry.Value;
                }
            }

            missing = MissingFields(fields);
        }

        var dimensions = await _dimensionClassifier.ClassifyAsync(snapshot, fields, missing, relatedPages, cancellationToken)
            .ConfigureAwait(false);
        var deterministicLinks = _linker.BuildLinks(snapshot, relatedPages, fields, dimensions.PrimaryDimension, dimensions.SecondaryDimensions);
        var allLinks = await _graphReasoner.RefineLinksAsync(snapshot, fields, deterministicLinks, relatedPages, cancellationToken)
            .ConfigureAwait(false);

        var status = missing.Count == 0 ? "complete" : "partial";
        var method = dimensions.Source;
        var subjectValue = fields.TryGetValue(char.ToUpperInvariant(dimensions.PrimaryDimension[0]) + dimensions.PrimaryDimension[1..], out var subject)
            ? subject
            : snapshot.FactText;
        var key = _keyBuilder.BuildScopeRelativeKey(dimensions.PrimaryDimension, subjectValue, snapshot.FactText, snapshot.EffectiveTimestamp);

        var content = _renderer.RenderLearnedPage(
            fields,
            dimensions.PrimaryDimension,
            dimensions.SecondaryDimensions,
            allLinks,
            status,
            method,
            missing,
            snapshot.SessionId,
            snapshot.TurnId,
            snapshot.EffectiveTimestamp);

        NormalizedCounter.Add(1);
        if (missing.Count > 0)
        {
            PartialCounter.Add(1);
        }

        return new MemoryPageNormalizationResult(
            content,
            fields,
            missing,
            dimensions.PrimaryDimension,
            dimensions.SecondaryDimensions,
            dimensions.DimensionScores,
            allLinks,
            method,
            key);
    }

    private static List<string> MissingFields(IReadOnlyDictionary<string, string?> fields)
    {
        return MemoryPageFields.FiveWOneH
            .Where(field => !fields.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
            .ToList();
    }
}

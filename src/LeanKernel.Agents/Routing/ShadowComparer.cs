using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents.Routing;

/// <summary>
/// Produces deterministic comparison metadata for primary and shadow responses.
/// </summary>
public sealed class ShadowComparer(IOptions<LeanKernelConfig> config)
{
    private readonly IReadOnlyList<string> _refusalPatterns = config?.Value.Routing.RefusalPatterns
        ?.Where(static pattern => !string.IsNullOrWhiteSpace(pattern))
        .Select(static pattern => pattern.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray() ?? throw new ArgumentNullException(nameof(config));

    /// <summary>
    /// Compares the authoritative and shadow responses.
    /// </summary>
    /// <param name="primaryResponse">The authoritative primary response.</param>
    /// <param name="shadowResponse">The shadow response.</param>
    /// <returns>The derived comparison result.</returns>
    public ShadowComparison Compare(string primaryResponse, string shadowResponse)
    {
        primaryResponse ??= string.Empty;
        shadowResponse ??= string.Empty;

        var primaryLength = primaryResponse.Length;
        var shadowLength = shadowResponse.Length;
        var lengthRatio = shadowLength / (double)Math.Max(1, primaryLength);
        var primaryRefusal = ResponseQualityHeuristics.LooksLikeRefusal(primaryResponse, _refusalPatterns);
        var shadowRefusal = ResponseQualityHeuristics.LooksLikeRefusal(shadowResponse, _refusalPatterns);
        var notes = BuildNotes(primaryLength, shadowLength, primaryRefusal, shadowRefusal);

        return new ShadowComparison
        {
            LengthRatio = lengthRatio,
            BothNonEmpty = !string.IsNullOrWhiteSpace(primaryResponse) && !string.IsNullOrWhiteSpace(shadowResponse),
            PrimaryRefusal = primaryRefusal,
            ShadowRefusal = shadowRefusal,
            Notes = notes,
        };
    }

    private static string? BuildNotes(int primaryLength, int shadowLength, bool primaryRefusal, bool shadowRefusal)
    {
        var notes = new List<string>();

        if (primaryLength > 0 && shadowLength >= (primaryLength * 3) / 2)
        {
            notes.Add("shadow significantly longer");
        }
        else if (shadowLength > 0 && primaryLength >= (shadowLength * 3) / 2)
        {
            notes.Add("shadow significantly shorter");
        }

        if (shadowRefusal && !primaryRefusal)
        {
            notes.Add("shadow refused but primary didn't");
        }
        else if (primaryRefusal && !shadowRefusal)
        {
            notes.Add("primary refused but shadow didn't");
        }

        return notes.Count == 0 ? null : string.Join("; ", notes);
    }
}

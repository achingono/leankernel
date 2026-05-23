using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents.Enhancement;

/// <summary>
/// Softens benign false refusals without re-invoking the model.
/// </summary>
public sealed class RefusalInterceptionStep(IOptions<LeanKernelConfig> config) : IEnhancementStep
{
    private static readonly string[] UnsafeKeywords =
    [
        "bomb",
        "bypass",
        "crack",
        "ddos",
        "exploit",
        "fraud",
        "hack",
        "harm",
        "malware",
        "password",
        "phishing",
        "ransomware",
        "steal",
        "suicide",
        "weapon"
    ];

    private const string RetryNote = "I wasn't able to fully address this. Let me try a different approach.";

    private readonly string[] _patterns = (config ?? throw new ArgumentNullException(nameof(config))).Value.Routing.RefusalPatterns
        .Where(static pattern => !string.IsNullOrWhiteSpace(pattern))
        .Select(static pattern => pattern.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <inheritdoc />
    public string Name => "refusal-interception";

    /// <inheritdoc />
    public int Order => 20;

    /// <inheritdoc />
    public Task<EnhancementStepOutput> ExecuteAsync(EnhancementStepInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (_patterns.Length == 0)
        {
            return Task.FromResult(CreateNoChange(input.Response, "No refusal patterns were configured."));
        }

        var matchedPattern = _patterns.FirstOrDefault(pattern =>
            input.Response.Contains(pattern, StringComparison.OrdinalIgnoreCase));
        if (matchedPattern is null)
        {
            return Task.FromResult(CreateNoChange(input.Response, "No refusal pattern was detected."));
        }

        if (!IsBenign(input.UserMessage))
        {
            return Task.FromResult(CreateNoChange(input.Response, "User message was not considered benign."));
        }

        if (input.Response.Contains(RetryNote, StringComparison.Ordinal))
        {
            return Task.FromResult(CreateNoChange(input.Response, "Retry note already present."));
        }

        return Task.FromResult(new EnhancementStepOutput
        {
            Response = $"{input.Response.TrimEnd()}\n\n{RetryNote}",
            Modified = true,
            Reason = $"Matched refusal pattern '{matchedPattern}'."
        });
    }

    private static bool IsBenign(string userMessage)
        => UnsafeKeywords.All(keyword => !userMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static EnhancementStepOutput CreateNoChange(string response, string reason)
        => new()
        {
            Response = response,
            Modified = false,
            Reason = reason
        };
}

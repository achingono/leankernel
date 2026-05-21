using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Thinker.Enhancement;

/// <summary>
/// Normalizes model-style exam answer wrappers for non-math conversations.
/// </summary>
public sealed class ResponseFormatGuardEnhancer : IResponseEnhancer
{
    private readonly ILogger<ResponseFormatGuardEnhancer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResponseFormatGuardEnhancer" /> class.
    /// </summary>
    /// <param name="logger">The logger used for diagnostics.</param>
    public ResponseFormatGuardEnhancer(ILogger<ResponseFormatGuardEnhancer> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<string> EnhanceResponseAsync(
        string userQuery,
        string assistantResponse,
        ConversationContext context,
        CancellationToken ct)
    {
        var containsBoxedMath = ResponseFormatHeuristics.ContainsBoxedMath(assistantResponse);
        var containsExamWrapper = ResponseFormatHeuristics.ContainsExamWrapper(assistantResponse);
        if (!containsBoxedMath && !containsExamWrapper)
        {
            return Task.FromResult(assistantResponse);
        }

        var isMathContext = ResponseFormatHeuristics.IsMathContext(userQuery, context);
        if (isMathContext)
        {
            return Task.FromResult(assistantResponse);
        }

        var rewritten = ResponseFormatHeuristics.NormalizeNonMathArtifacts(assistantResponse);
        if (string.IsNullOrWhiteSpace(rewritten))
        {
            return Task.FromResult(assistantResponse);
        }

        if (!string.Equals(rewritten, assistantResponse, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Response format guard normalized output (examWrapper={ExamWrapper}, boxedMath={BoxedMath})",
                containsExamWrapper,
                containsBoxedMath);
        }

        return Task.FromResult(rewritten);
    }
}

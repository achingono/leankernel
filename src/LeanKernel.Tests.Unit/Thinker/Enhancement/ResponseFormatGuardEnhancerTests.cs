using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Core.Models;
using LeanKernel.Thinker.Enhancement;

namespace LeanKernel.Tests.Unit.Thinker.Enhancement;

public sealed class ResponseFormatGuardEnhancerTests
{
    [Fact]
    public async Task EnhanceResponseAsync_NonMathExamWrapper_NormalizesResponse()
    {
        var enhancer = new ResponseFormatGuardEnhancer(NullLogger<ResponseFormatGuardEnhancer>.Instance);
        var context = BuildContext();

        var result = await enhancer.EnhanceResponseAsync(
            "Is this true for my situation?",
            "The final answer is: $\\boxed{0}$",
            context,
            CancellationToken.None);

        Assert.Equal("0", result);
    }

    [Fact]
    public async Task EnhanceResponseAsync_MathContext_PreservesBoxedOutput()
    {
        var enhancer = new ResponseFormatGuardEnhancer(NullLogger<ResponseFormatGuardEnhancer>.Instance);
        var context = BuildContext();

        var result = await enhancer.EnhanceResponseAsync(
            "Solve this equation and format in LaTeX.",
            "The final answer is: $\\boxed{x=2}$",
            context,
            CancellationToken.None);

        Assert.Equal("The final answer is: $\\boxed{x=2}$", result);
    }

    [Fact]
    public async Task EnhanceResponseAsync_CodeFence_PreservesBoxedInsideFence()
    {
        var enhancer = new ResponseFormatGuardEnhancer(NullLogger<ResponseFormatGuardEnhancer>.Instance);
        var context = BuildContext();

        var result = await enhancer.EnhanceResponseAsync(
            "Share the exact snippet.",
            """
            Use this snippet:
            ```latex
            The final answer is: \boxed{0}
            ```
            """,
            context,
            CancellationToken.None);

        Assert.Contains("The final answer is: \\boxed{0}", result);
    }

    [Fact]
    public async Task EnhanceResponseAsync_NoArtifacts_PassesThrough()
    {
        var enhancer = new ResponseFormatGuardEnhancer(NullLogger<ResponseFormatGuardEnhancer>.Instance);
        var context = BuildContext();
        const string response = "This is a normal response.";

        var result = await enhancer.EnhanceResponseAsync(
            "Tell me more.",
            response,
            context,
            CancellationToken.None);

        Assert.Equal(response, result);
    }

    private static ConversationContext BuildContext() =>
        new()
        {
            SystemPrompt = "test",
            History = [],
            WikiLeanKernels = [],
            RetrievedLeanKernels = [],
            ActiveToolNames = []
        };
}

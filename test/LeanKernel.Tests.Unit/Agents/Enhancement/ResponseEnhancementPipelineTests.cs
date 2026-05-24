using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Enhancement;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Agents.Enhancement;

public class ResponseEnhancementPipelineTests
{
    [Fact]
    public async Task EnhanceAsync_runs_steps_in_order_and_uses_previous_output()
    {
        var pipeline = CreatePipeline(
        [
            new AppendStep("second", 20, "B"),
            new AppendStep("first", 10, "A")
        ]);

        var result = await pipeline.EnhanceAsync(new EnhancementStepInput
        {
            Response = "start",
            UserMessage = "Summarize Atlas"
        });

        result.EnhancedResponse.Should().Be("startAB");
        result.WasModified.Should().BeTrue();
        result.Steps.Select(step => step.StepName).Should().Equal("first", "second");
        result.Steps.Should().OnlyContain(step => step.Applied);
    }

    [Fact]
    public async Task EnhanceAsync_returns_original_response_when_a_step_fails()
    {
        var pipeline = CreatePipeline(
        [
            new AppendStep("first", 10, "A"),
            new ThrowingStep("boom", 20)
        ]);

        var result = await pipeline.EnhanceAsync(new EnhancementStepInput
        {
            Response = "start",
            UserMessage = "Summarize Atlas"
        });

        result.EnhancedResponse.Should().Be("start");
        result.WasModified.Should().BeFalse();
        result.Steps.Should().HaveCount(2);
        result.Steps[0].Modified.Should().BeTrue();
        result.Steps[1].Applied.Should().BeFalse();
    }

    [Fact]
    public async Task EnhanceAsync_returns_original_response_when_timeout_is_exceeded()
    {
        var pipeline = CreatePipeline(
            [new DelayedStep("slow", 10, TimeSpan.FromMilliseconds(50))],
            new EnhancementConfig { MaxEnhancementTimeMs = 5 });

        var result = await pipeline.EnhanceAsync(new EnhancementStepInput
        {
            Response = "start",
            UserMessage = "Summarize Atlas"
        });

        result.EnhancedResponse.Should().Be("start");
        result.WasModified.Should().BeFalse();
        result.Steps.Should().ContainSingle();
        result.Steps[0].Applied.Should().BeFalse();
        result.Steps[0].Reason.Should().ContainEquivalentOf("timed out");
    }

    [Fact]
    public async Task EnhanceAsync_returns_original_response_when_no_steps_are_enabled()
    {
        var pipeline = CreatePipeline([]);

        var result = await pipeline.EnhanceAsync(new EnhancementStepInput
        {
            Response = "start",
            UserMessage = "Summarize Atlas"
        });

        result.EnhancedResponse.Should().Be("start");
        result.WasModified.Should().BeFalse();
        result.Steps.Should().BeEmpty();
    }

    private static ResponseEnhancementPipeline CreatePipeline(
        IEnumerable<IEnhancementStep> steps,
        EnhancementConfig? enhancementConfig = null)
        => new(
            steps,
            Options.Create(new LeanKernelConfig
            {
                Enhancement = enhancementConfig ?? new EnhancementConfig()
            }),
            NullLogger<ResponseEnhancementPipeline>.Instance);

    private sealed class AppendStep(string name, int order, string suffix) : IEnhancementStep
    {
        public string Name => name;

        public int Order => order;

        public Task<EnhancementStepOutput> ExecuteAsync(EnhancementStepInput input, CancellationToken ct = default)
            => Task.FromResult(new EnhancementStepOutput
            {
                Response = input.Response + suffix,
                Modified = true,
                Reason = $"Appended {suffix}."
            });
    }

    private sealed class ThrowingStep(string name, int order) : IEnhancementStep
    {
        public string Name => name;

        public int Order => order;

        public Task<EnhancementStepOutput> ExecuteAsync(EnhancementStepInput input, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }

    private sealed class DelayedStep(string name, int order, TimeSpan delay) : IEnhancementStep
    {
        public string Name => name;

        public int Order => order;

        public async Task<EnhancementStepOutput> ExecuteAsync(EnhancementStepInput input, CancellationToken ct = default)
        {
            await Task.Delay(delay, ct);
            return new EnhancementStepOutput
            {
                Response = input.Response + "!",
                Modified = true,
                Reason = "Delayed append."
            };
        }
    }
}

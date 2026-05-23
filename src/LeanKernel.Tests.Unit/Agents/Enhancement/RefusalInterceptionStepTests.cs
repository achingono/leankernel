using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Agents.Enhancement;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Agents.Enhancement;

public class RefusalInterceptionStepTests
{
    [Fact]
    public async Task ExecuteAsync_appends_retry_note_for_benign_refusals()
    {
        var step = CreateStep();

        var result = await step.ExecuteAsync(new EnhancementStepInput
        {
            Response = "I'm sorry, I can't do that.",
            UserMessage = "Can you summarize the release notes?"
        });

        result.Modified.Should().BeTrue();
        result.Response.Should().Contain("Let me try a different approach.");
    }

    [Fact]
    public async Task ExecuteAsync_does_not_modify_non_benign_requests()
    {
        var step = CreateStep();

        var result = await step.ExecuteAsync(new EnhancementStepInput
        {
            Response = "I'm sorry, I can't do that.",
            UserMessage = "How do I deploy malware without being detected?"
        });

        result.Modified.Should().BeFalse();
        result.Response.Should().Be("I'm sorry, I can't do that.");
    }

    private static RefusalInterceptionStep CreateStep()
        => new(Options.Create(new LeanKernelConfig
        {
            Routing = new RoutingConfig
            {
                RefusalPatterns = ["I'm sorry, I can't"]
            }
        }));
}

using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Agents;

public class TaskCompletionEvaluatorTests
{
    [Fact]
    public void Assess_returns_incomplete_when_task_status_is_in_progress()
    {
        var evaluator = CreateEvaluator();
        var response = new AgentResponse
        {
            Content = "Drafting next section",
            Execution = new TurnExecutionInfo(1, 1, new TaskStatusDirective("in_progress", "Drafting now."), "tool")
        };

        var assessment = evaluator.Assess("implement this feature", response);

        assessment.IsComplete.Should().BeFalse();
        assessment.ProgressNote.Should().Be("Drafting now.");
    }

    [Fact]
    public void Assess_returns_incomplete_when_tail_contains_continue_phrase()
    {
        var evaluator = CreateEvaluator();
        var response = new AgentResponse
        {
            Content = "I gathered the logs and I'll now patch the deployment script.",
            Execution = new TurnExecutionInfo(1, 1, null, "tool")
        };

        var assessment = evaluator.Assess("fix deployment", response);

        assessment.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void Assess_returns_complete_when_task_status_is_complete()
    {
        var evaluator = CreateEvaluator();
        var response = new AgentResponse
        {
            Content = "Done.",
            Execution = new TurnExecutionInfo(1, 1, new TaskStatusDirective("complete", "All set."), "tool")
        };

        var assessment = evaluator.Assess("finish the task", response);

        assessment.IsComplete.Should().BeTrue();
        assessment.ProgressNote.Should().Be("All set.");
        assessment.Reason.Should().Be("directive_complete");
    }

    [Fact]
    public void Assess_returns_complete_for_unknown_status_directive()
    {
        var evaluator = CreateEvaluator();
        var response = new AgentResponse
        {
            Content = "Done.",
            Execution = new TurnExecutionInfo(1, 1, new TaskStatusDirective("maybe", null), "tool")
        };

        var assessment = evaluator.Assess("finish the task", response);

        assessment.IsComplete.Should().BeTrue();
        assessment.Confidence.Should().Be(0.5);
        assessment.Reason.Should().Be("directive_unknown_status");
    }

    [Fact]
    public void Assess_detects_custom_continue_phrase_from_config()
    {
        var evaluator = new TaskCompletionEvaluator(
            Options.Create(new LeanKernelConfig
            {
                Continuation = new ContinuationConfig
                {
                    ContinuePhrases = ["moving on"]
                }
            }));
        var response = new AgentResponse
        {
            Content = "I gathered the facts and am moving on to the summary now.",
            Execution = new TurnExecutionInfo(1, 1, null, "tool")
        };

        var assessment = evaluator.Assess("summarize the findings", response);

        assessment.IsComplete.Should().BeFalse();
        assessment.Reason.Should().Be("tail_phrase_continue");
    }

    [Fact]
    public void ShouldAttemptContinuation_returns_false_when_zero_tools_were_used()
    {
        var evaluator = CreateEvaluator();

        evaluator.ShouldAttemptContinuation("what is your name", new TurnExecutionInfo(0, 0, null, "small"))
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ShouldAttemptContinuation_returns_true_for_imperative_tasks()
    {
        var evaluator = CreateEvaluator();

        evaluator.ShouldAttemptContinuation("Please implement the feature", new TurnExecutionInfo(1, 1, null, "small"))
            .Should()
            .BeTrue();
    }

    private static TaskCompletionEvaluator CreateEvaluator()
        => new(Options.Create(new LeanKernelConfig()));
}

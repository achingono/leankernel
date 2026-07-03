using System.Text.RegularExpressions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents;

/// <summary>
/// Evaluates whether a response appears to have completed the user's requested task.
/// </summary>
public sealed class TaskCompletionEvaluator(IOptions<LeanKernelConfig> config)
{
    private static readonly string[] BuiltInContinuePhrases =
    [
        "i'll now",
        "next i will",
        "next, i will",
        "proceeding to",
        "let me continue",
        "i will continue",
        "continuing with",
        "i'll continue",
    ];

    private readonly ContinuationConfig _continuation = (config ?? throw new ArgumentNullException(nameof(config))).Value.Continuation;

    public TaskCompletionAssessment Assess(string userMessage, AgentResponse response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage);
        ArgumentNullException.ThrowIfNull(response);

        var taskStatus = response.Execution?.TaskStatus;
        if (taskStatus is not null)
        {
            var status = taskStatus.Status.Trim().ToLowerInvariant();
            return status switch
            {
                "complete" => new TaskCompletionAssessment(true, 1.0, taskStatus.Note, "directive_complete"),
                "in_progress" => new TaskCompletionAssessment(false, 1.0, taskStatus.Note, "directive_in_progress"),
                "blocked" => new TaskCompletionAssessment(false, 1.0, taskStatus.Note, "directive_blocked"),
                _ => new TaskCompletionAssessment(true, 0.5, null, "directive_unknown_status"),
            };
        }

        var tail = Tail(response.Content, 300).ToLowerInvariant();
        if (MatchesContinuationPhrase(tail))
        {
            return new TaskCompletionAssessment(false, 0.75, null, "tail_phrase_continue");
        }

        return new TaskCompletionAssessment(true, 0.6, null, "default_complete");
    }

    public bool ShouldAttemptContinuation(string userMessage, TurnExecutionInfo? execution)
    {
        if (execution is null || execution.ToolInvocationCount <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return false;
        }

        var text = userMessage.Trim();
        if (text.Length >= 80)
        {
            return true;
        }

        return Regex.IsMatch(
            text,
            "\\b(build|create|fix|investigate|deploy|implement|analyze|review|update|refactor|write|run|test|validate|search|continue)\\b",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));
    }

    private bool MatchesContinuationPhrase(string tail)
    {
        foreach (var phrase in BuiltInContinuePhrases)
        {
            if (tail.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var phrase in _continuation.ContinuePhrases)
        {
            if (!string.IsNullOrWhiteSpace(phrase)
                && tail.Contains(phrase.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string Tail(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        return value[^maxChars..];
    }
}

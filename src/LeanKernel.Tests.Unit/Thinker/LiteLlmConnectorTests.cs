using LeanKernel.Core.Models;
using LeanKernel.Thinker.SemanticKernel;

namespace LeanKernel.Tests.Unit.Thinker;

public class LiteLlmConnectorTests
{
    [Fact]
    public void BuildChatHistory_IncludesSystemPrompt()
    {
        var context = CreateMinimalContext("You are LeanKernel.");
        var history = LiteLlmConnector.BuildChatHistory(context, "Hello");

        Assert.True(history.Count >= 2); // system + user
        Assert.Equal(Microsoft.SemanticKernel.ChatCompletion.AuthorRole.System, history[0].Role);
        Assert.Contains("You are LeanKernel.", history[0].Content);
    }

    [Fact]
    public void BuildChatHistory_InjectsWikiLeanKernels()
    {
        var context = CreateMinimalContext("System prompt");
        context = context with
        {
            WikiLeanKernels =
            [
                new RelevanceScore
                {
                    EntryId = "who-alice",
                    Content = "[Who:Alice] Engineer at Acme",
                    EstimatedTokens = 10,
                    Score = 0.9
                }
            ]
        };

        var history = LiteLlmConnector.BuildChatHistory(context, "Tell me about Alice");
        var systemMsg = history[0].Content;

        Assert.Contains("Alice", systemMsg);
        Assert.Contains("Relevant Knowledge", systemMsg);
    }

    [Fact]
    public void BuildChatHistory_IncludesConversationHistory()
    {
        var context = CreateMinimalContext("System") with
        {
            History =
            [
                new ConversationTurn { Role = "user", Content = "Hi", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5) },
                new ConversationTurn { Role = "assistant", Content = "Hello!", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-4) }
            ]
        };

        var history = LiteLlmConnector.BuildChatHistory(context, "How are you?");

        // system + 2 history turns + current user query = 4
        Assert.Equal(4, history.Count);
    }

    [Fact]
    public void BuildChatHistory_CurrentQueryIsLast()
    {
        var context = CreateMinimalContext("System");
        var history = LiteLlmConnector.BuildChatHistory(context, "What time is it?");

        var lastMsg = history[^1];
        Assert.Equal(Microsoft.SemanticKernel.ChatCompletion.AuthorRole.User, lastMsg.Role);
        Assert.Equal("What time is it?", lastMsg.Content);
    }

    [Fact]
    public void BuildChatHistory_IncludesToolNames()
    {
        var context = CreateMinimalContext("System") with
        {
            ActiveToolNames = ["web_search", "wiki_query"]
        };

        var history = LiteLlmConnector.BuildChatHistory(context, "Search for X");
        var systemMsg = history[0].Content;

        Assert.Contains("web_search", systemMsg);
        Assert.Contains("Available Tools", systemMsg);
    }

    private static ConversationContext CreateMinimalContext(string systemPrompt) => new()
    {
        SystemPrompt = systemPrompt,
        History = [],
        WikiLeanKernels = [],
        RetrievedLeanKernels = [],
        ActiveToolNames = []
    };
}

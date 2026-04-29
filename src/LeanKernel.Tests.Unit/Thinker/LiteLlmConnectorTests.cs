using Microsoft.Extensions.AI;
using LeanKernel.Core.Models;
using LeanKernel.Thinker;

namespace LeanKernel.Tests.Unit.Thinker;

/// <summary>
/// Tests for ThinkerService.BuildMessages — the method that converts
/// gated ConversationContext into MEAI ChatMessage format.
/// (Replaces former LiteLlmConnectorTests)
/// </summary>
public class ThinkerServiceBuildMessagesTests
{
    [Fact]
    public void BuildMessages_EmptyHistory_ReturnsSingleUserMessage()
    {
        var messages = ThinkerService.BuildMessages([], "Hello").ToList();

        Assert.Single(messages);
        Assert.Equal(ChatRole.User, messages[0].Role);
        Assert.Equal("Hello", messages[0].Text);
    }

    [Fact]
    public void BuildMessages_WithHistory_IncludesAllTurns()
    {
        var history = new List<ConversationTurn>
        {
            new() { Role = "user", Content = "Hi", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5) },
            new() { Role = "assistant", Content = "Hello!", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-4) }
        };

        var messages = ThinkerService.BuildMessages(history, "How are you?").ToList();

        // 2 history turns + current query = 3
        Assert.Equal(3, messages.Count);
        Assert.Equal(ChatRole.User, messages[0].Role);
        Assert.Equal("Hi", messages[0].Text);
        Assert.Equal(ChatRole.Assistant, messages[1].Role);
        Assert.Equal("Hello!", messages[1].Text);
        Assert.Equal(ChatRole.User, messages[2].Role);
        Assert.Equal("How are you?", messages[2].Text);
    }

    [Fact]
    public void BuildMessages_CurrentQueryIsLast()
    {
        var history = new List<ConversationTurn>
        {
            new() { Role = "user", Content = "Previous", Timestamp = DateTimeOffset.UtcNow }
        };

        var messages = ThinkerService.BuildMessages(history, "What time is it?").ToList();

        var last = messages[^1];
        Assert.Equal(ChatRole.User, last.Role);
        Assert.Equal("What time is it?", last.Text);
    }

    [Fact]
    public void BuildMessages_PreservesRoleMapping()
    {
        var history = new List<ConversationTurn>
        {
            new() { Role = "user", Content = "msg1", Timestamp = DateTimeOffset.UtcNow },
            new() { Role = "assistant", Content = "msg2", Timestamp = DateTimeOffset.UtcNow },
            new() { Role = "user", Content = "msg3", Timestamp = DateTimeOffset.UtcNow }
        };

        var messages = ThinkerService.BuildMessages(history, "msg4").ToList();

        Assert.Equal(4, messages.Count);
        Assert.Equal(ChatRole.User, messages[0].Role);
        Assert.Equal(ChatRole.Assistant, messages[1].Role);
        Assert.Equal(ChatRole.User, messages[2].Role);
        Assert.Equal(ChatRole.User, messages[3].Role);
    }
}

using System;
using FluentAssertions;
using LeanKernel.Agents;
using Microsoft.Extensions.AI;
using Xunit;

namespace LeanKernel.Tests.Unit.Agents;

public class ChatResponseMetadataReaderTests
{
    private class TestChatResponse : ChatResponse
    {
        public new object? Usage { get; set; }

        public TestChatResponse(ChatMessage message) : base(message)
        {
        }
    }

    private class UsageWithTotalTokenCountInt
    {
        public int TotalTokenCount { get; set; }
    }

    private class UsageWithTotalTokenCountLong
    {
        public long TotalTokenCount { get; set; }
    }

    private class UsageWithTotalTokens
    {
        public int TotalTokens { get; set; }
    }

    private class UsageWithInputOutputCount
    {
        public int InputTokenCount { get; set; }
        public int OutputTokenCount { get; set; }
    }

    private class UsageWithInputOutputTokens
    {
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
    }

    [Fact]
    public void GetTokensUsed_Throws_On_Null_Response()
    {
        Assert.Throws<ArgumentNullException>(() => ChatResponseMetadataReader.GetTokensUsed(null!));
    }

    [Fact]
    public void GetTokensUsed_Returns_Zero_When_No_Usage_Property()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "hello"));
        ChatResponseMetadataReader.GetTokensUsed(response).Should().Be(0);
    }

    [Fact]
    public void GetTokensUsed_Reads_TotalTokenCount_Int()
    {
        var response = new TestChatResponse(new ChatMessage(ChatRole.Assistant, "hello"))
        {
            Usage = new UsageWithTotalTokenCountInt { TotalTokenCount = 123 }
        };

        ChatResponseMetadataReader.GetTokensUsed(response).Should().Be(123);
    }

    [Fact]
    public void GetTokensUsed_Reads_TotalTokenCount_Long()
    {
        var response = new TestChatResponse(new ChatMessage(ChatRole.Assistant, "hello"))
        {
            Usage = new UsageWithTotalTokenCountLong { TotalTokenCount = 456L }
        };

        ChatResponseMetadataReader.GetTokensUsed(response).Should().Be(456);
    }

    [Fact]
    public void GetTokensUsed_Reads_TotalTokens()
    {
        var response = new TestChatResponse(new ChatMessage(ChatRole.Assistant, "hello"))
        {
            Usage = new UsageWithTotalTokens { TotalTokens = 789 }
        };

        ChatResponseMetadataReader.GetTokensUsed(response).Should().Be(789);
    }

    [Fact]
    public void GetTokensUsed_Calculates_InputOutputTokenCount()
    {
        var response = new TestChatResponse(new ChatMessage(ChatRole.Assistant, "hello"))
        {
            Usage = new UsageWithInputOutputCount { InputTokenCount = 10, OutputTokenCount = 20 }
        };

        ChatResponseMetadataReader.GetTokensUsed(response).Should().Be(30);
    }

    [Fact]
    public void GetTokensUsed_Calculates_InputOutputTokens()
    {
        var response = new TestChatResponse(new ChatMessage(ChatRole.Assistant, "hello"))
        {
            Usage = new UsageWithInputOutputTokens { InputTokens = 100L, OutputTokens = 200L }
        };

        ChatResponseMetadataReader.GetTokensUsed(response).Should().Be(300);
    }

    [Fact]
    public void GetTokensUsed_Clamps_Large_Values()
    {
        var responseLarge = new TestChatResponse(new ChatMessage(ChatRole.Assistant, "hello"))
        {
            Usage = new UsageWithTotalTokenCountLong { TotalTokenCount = (long)int.MaxValue + 10 }
        };

        ChatResponseMetadataReader.GetTokensUsed(responseLarge).Should().Be(int.MaxValue);

        var responseSmall = new TestChatResponse(new ChatMessage(ChatRole.Assistant, "hello"))
        {
            Usage = new UsageWithTotalTokenCountLong { TotalTokenCount = (long)int.MinValue - 10 }
        };

        ChatResponseMetadataReader.GetTokensUsed(responseSmall).Should().Be(int.MinValue);
    }
}

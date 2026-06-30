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
        public TestChatResponse(ChatMessage message) : base(message)
        {
        }
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
            Usage = new UsageDetails { TotalTokenCount = 123 }
        };

        ChatResponseMetadataReader.GetTokensUsed(response).Should().Be(123);
    }

    [Fact]
    public void GetTokensUsed_Reads_TotalTokenCount_Long()
    {
        var response = new TestChatResponse(new ChatMessage(ChatRole.Assistant, "hello"))
        {
            Usage = new UsageDetails { TotalTokenCount = 456L }
        };

        ChatResponseMetadataReader.GetTokensUsed(response).Should().Be(456);
    }

    [Fact]
    public void GetTokensUsed_Calculates_InputOutputTokenCount()
    {
        var response = new TestChatResponse(new ChatMessage(ChatRole.Assistant, "hello"))
        {
            Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 20 }
        };

        ChatResponseMetadataReader.GetTokensUsed(response).Should().Be(30);
    }

    [Fact]
    public void GetTokensUsed_Clamps_Large_Values()
    {
        var responseLarge = new TestChatResponse(new ChatMessage(ChatRole.Assistant, "hello"))
        {
            Usage = new UsageDetails { TotalTokenCount = (long)int.MaxValue + 10 }
        };

        ChatResponseMetadataReader.GetTokensUsed(responseLarge).Should().Be(int.MaxValue);

        var responseSmall = new TestChatResponse(new ChatMessage(ChatRole.Assistant, "hello"))
        {
            Usage = new UsageDetails { TotalTokenCount = (long)int.MinValue - 10 }
        };

        ChatResponseMetadataReader.GetTokensUsed(responseSmall).Should().Be(int.MinValue);
    }
}

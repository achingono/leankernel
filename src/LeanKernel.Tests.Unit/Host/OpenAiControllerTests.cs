using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Host.Controllers;
using LeanKernel.Host.Services;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Host;

public class OpenAiControllerTests
{
    private static InboundAttachmentInputProcessor CreateAttachmentProcessor()
    {
        var extractor = Substitute.For<IAttachmentTextExtractionService>();
        extractor.ExtractTextAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
                Task.FromResult(InboundAttachmentTextExtractor.TryExtractText(
                    callInfo.ArgAt<string?>(0),
                    callInfo.ArgAt<string?>(1),
                    callInfo.ArgAt<byte[]>(2))));
        extractor.CanExtractText(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(callInfo => InboundAttachmentTextExtractor.CanExtractText(
                callInfo.ArgAt<string?>(0),
                callInfo.ArgAt<string?>(1)));

        return new InboundAttachmentInputProcessor(extractor);
    }

    [Fact]
    public async Task ChatCompletions_ReturnsOpenAiFormat()
    {
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("AI response");

        var controller = new OpenAiController(thinker, NullLogger<OpenAiController>.Instance, CreateAttachmentProcessor());
        var request = new OpenAiChatRequest
        {
            Model = "LeanKernel",
            Messages = [new OpenAiMessage { Role = "user", Content = "Hello" }]
        };

        var result = await controller.ChatCompletions(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<OpenAiChatResponse>(ok.Value);
        Assert.Equal("chat.completion", response.Object);
        Assert.Single(response.Choices);
        Assert.Equal("AI response", response.Choices[0].Message.Content);
        Assert.Equal("stop", response.Choices[0].FinishReason);
    }

    [Fact]
    public async Task ChatCompletions_ExtractsLastUserMessage()
    {
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        var controller = new OpenAiController(thinker, NullLogger<OpenAiController>.Instance, CreateAttachmentProcessor());
        var request = new OpenAiChatRequest
        {
            Messages =
            [
                new OpenAiMessage { Role = "user", Content = "First" },
                new OpenAiMessage { Role = "assistant", Content = "Reply" },
                new OpenAiMessage { Role = "user", Content = "Second" }
            ]
        };

        await controller.ChatCompletions(request, CancellationToken.None);

        await thinker.Received(1).ProcessAsync(
            Arg.Is<LeanKernelMessage>(m => m.Content == "Second"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChatCompletions_CalculatesUsage()
    {
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("response text");

        var controller = new OpenAiController(thinker, NullLogger<OpenAiController>.Instance, CreateAttachmentProcessor());
        var request = new OpenAiChatRequest
        {
            Messages = [new OpenAiMessage { Role = "user", Content = "test input" }]
        };

        var result = await controller.ChatCompletions(request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<OpenAiChatResponse>(ok.Value);

        Assert.NotNull(response.Usage);
        Assert.True(response.Usage.PromptTokens > 0);
        Assert.True(response.Usage.CompletionTokens > 0);
        Assert.Equal(response.Usage.PromptTokens + response.Usage.CompletionTokens, response.Usage.TotalTokens);
    }

    [Fact]
    public async Task ChatCompletions_NullMessages_HandlesGracefully()
    {
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        var controller = new OpenAiController(thinker, NullLogger<OpenAiController>.Instance, CreateAttachmentProcessor());
        var request = new OpenAiChatRequest { Model = "LeanKernel" };

        var result = await controller.ChatCompletions(request, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ChatCompletions_UsesUserField()
    {
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        var controller = new OpenAiController(thinker, NullLogger<OpenAiController>.Instance, CreateAttachmentProcessor());
        var request = new OpenAiChatRequest
        {
            User = "custom-user",
            Messages = [new OpenAiMessage { Role = "user", Content = "Hi" }]
        };

        await controller.ChatCompletions(request, CancellationToken.None);
        await thinker.Received(1).ProcessAsync(
            Arg.Is<LeanKernelMessage>(m => m.SenderId == "custom-user"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChatCompletions_FormatsAttachmentContent()
    {
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        var controller = new OpenAiController(thinker, NullLogger<OpenAiController>.Instance, CreateAttachmentProcessor());
        var request = new OpenAiChatRequest
        {
            Messages =
            [
                new OpenAiMessage
                {
                    Role = "user",
                    Content = "Summarize this file.",
                    Attachments =
                    [
                        new InboundAttachmentInput
                        {
                            FileName = "summary.txt",
                            ContentType = "text/plain",
                            Text = "Alpha\nBeta"
                        }
                    ]
                }
            ]
        };

        await controller.ChatCompletions(request, CancellationToken.None);

        await thinker.Received(1).ProcessAsync(
            Arg.Is<LeanKernelMessage>(message =>
                message.Content.Contains("Received 1 attachment:")
                && message.Content.Contains("summary.txt")
                && message.Metadata["openai-api:attachment_count"] == "1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChatCompletions_InvalidAttachmentBase64_ReturnsBadRequest()
    {
        var controller = new OpenAiController(
            Substitute.For<IThinkerService>(),
            NullLogger<OpenAiController>.Instance,
            CreateAttachmentProcessor());

        var result = await controller.ChatCompletions(new OpenAiChatRequest
        {
            Messages =
            [
                new OpenAiMessage
                {
                    Role = "user",
                    Content = "test",
                    Attachments =
                    [
                        new InboundAttachmentInput
                        {
                            FileName = "broken.txt",
                            ContentType = "text/plain",
                            Base64Content = "%%%bad%%%"
                        }
                    ]
                }
            ]
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void ListModels_ReturnsLeanKernelModel()
    {
        var controller = new OpenAiController(
            Substitute.For<IThinkerService>(),
            NullLogger<OpenAiController>.Instance,
            CreateAttachmentProcessor());
        var result = controller.ListModels();

        Assert.IsType<OkObjectResult>(result);
    }
}

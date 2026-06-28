using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents;
using LeanKernel.Agents.ToolSelection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Agents.ToolSelection;

public class ToolSelectorTests
{
    [Fact]
    public async Task NullToolSelector_Returns_AllTools()
    {
        var selector = NullToolSelector.Instance;
        var tools = new List<ToolDefinition>
        {
            new() { Name = "Tool1", Description = "Desc1" },
            new() { Name = "Tool2", Description = "Desc2" }
        };

        var result = await selector.SelectToolsAsync("test", tools, 1);

        result.Should().BeEquivalentTo(tools);
    }

    [Fact]
    public void Constructor_Throws_On_Null_Args()
    {
        var chatClient = new Mock<IChatClient>().Object;
        var factory = new AgentFactory(chatClient, NullLogger<AgentFactory>.Instance);
        var options = Options.Create(new LeanKernelConfig());
        var logger = NullLogger<ToolSelector>.Instance;

        Assert.Throws<ArgumentNullException>(() => new ToolSelector(null!, options, logger));
        Assert.Throws<ArgumentNullException>(() => new ToolSelector(factory, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new ToolSelector(factory, options, null!));
    }

    [Fact]
    public async Task SelectToolsAsync_Throws_On_Null_Inputs()
    {
        var chatClient = new Mock<IChatClient>().Object;
        var factory = new AgentFactory(chatClient, NullLogger<AgentFactory>.Instance);
        var options = Options.Create(new LeanKernelConfig());
        var logger = NullLogger<ToolSelector>.Instance;
        var selector = new ToolSelector(factory, options, logger);

        await Assert.ThrowsAsync<ArgumentNullException>(() => selector.SelectToolsAsync(null!, new List<ToolDefinition>(), 1));
        
        Func<Task> act = async () => await selector.SelectToolsAsync("test", null!, 1);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SelectToolsAsync_Returns_AllTools_If_Count_LessThanOrEqualTo_Max()
    {
        var chatClient = new Mock<IChatClient>().Object;
        var factory = new AgentFactory(chatClient, NullLogger<AgentFactory>.Instance);
        var options = Options.Create(new LeanKernelConfig());
        var logger = NullLogger<ToolSelector>.Instance;
        var selector = new ToolSelector(factory, options, logger);

        var tools = new List<ToolDefinition>
        {
            new() { Name = "Tool1", Description = "Desc1" },
            new() { Name = "Tool2", Description = "Desc2" }
        };

        var result = await selector.SelectToolsAsync("test", tools, 3);
        result.Should().BeEquivalentTo(tools);
    }

    [Fact]
    public async Task SelectToolsAsync_Uses_EconomyModel_And_Returns_Selected_Tools()
    {
        var config = new LeanKernelConfig();
        config.Routing.Economy.Model = "eco-model";
        var options = Options.Create(config);

        var chatClient = new Mock<IChatClient>();
        var ecoChatClient = new Mock<IChatClient>();
        var chatClients = new Dictionary<string, IChatClient>
        {
            { "eco-model", ecoChatClient.Object }
        };
        var factory = new AgentFactory(chatClient.Object, NullLogger<AgentFactory>.Instance, chatClients);

        var tools = new List<ToolDefinition>
        {
            new() { Name = "Tool1", Description = "Desc1" },
            new() { Name = "Tool2", Description = "Desc2" },
            new() { Name = "Tool3", Description = "Desc3" }
        };

        var responseText = "[\"Tool1\", \"Tool3\"]";
        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText));
        ecoChatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        var selector = new ToolSelector(factory, options, NullLogger<ToolSelector>.Instance);
        var result = await selector.SelectToolsAsync("find things", tools, 2);

        result.Should().HaveCount(2);
        result.Should().Contain(t => t.Name == "Tool1");
        result.Should().Contain(t => t.Name == "Tool3");
    }

    [Fact]
    public async Task SelectToolsAsync_Strips_Markdown_Fences_And_Selects_Tools()
    {
        var config = new LeanKernelConfig();
        config.Routing.Economy.Model = "eco-model";
        var options = Options.Create(config);

        var chatClient = new Mock<IChatClient>();
        var ecoChatClient = new Mock<IChatClient>();
        var chatClients = new Dictionary<string, IChatClient>
        {
            { "eco-model", ecoChatClient.Object }
        };
        var factory = new AgentFactory(chatClient.Object, NullLogger<AgentFactory>.Instance, chatClients);

        var tools = new List<ToolDefinition>
        {
            new() { Name = "Tool1", Description = "Desc1" },
            new() { Name = "Tool2", Description = "Desc2" }
        };

        var responseText = "```json\n[\"Tool2\"]\n```";
        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText));
        ecoChatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        var selector = new ToolSelector(factory, options, NullLogger<ToolSelector>.Instance);
        var result = await selector.SelectToolsAsync("find things", tools, 1);

        result.Should().HaveCount(1);
        result.Should().Contain(t => t.Name == "Tool2");
    }

    [Fact]
    public async Task SelectToolsAsync_FallsBack_On_Invalid_Json()
    {
        var config = new LeanKernelConfig();
        config.Routing.Economy.Model = "eco-model";
        var options = Options.Create(config);

        var chatClient = new Mock<IChatClient>();
        var ecoChatClient = new Mock<IChatClient>();
        var chatClients = new Dictionary<string, IChatClient>
        {
            { "eco-model", ecoChatClient.Object }
        };
        var factory = new AgentFactory(chatClient.Object, NullLogger<AgentFactory>.Instance, chatClients);

        var tools = new List<ToolDefinition>
        {
            new() { Name = "Tool1", Description = "Desc1" },
            new() { Name = "Tool2", Description = "Desc2" }
        };

        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, "invalid json"));
        ecoChatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatResponse);

        var selector = new ToolSelector(factory, options, NullLogger<ToolSelector>.Instance);
        var result = await selector.SelectToolsAsync("find things", tools, 1);

        result.Should().HaveCount(1);
        result.Should().Contain(t => t.Name == "Tool1");
    }

    [Fact]
    public async Task SelectToolsAsync_FallsBack_On_Exception()
    {
        var config = new LeanKernelConfig();
        config.Routing.Economy.Model = "eco-model";
        var options = Options.Create(config);

        var chatClient = new Mock<IChatClient>();
        var ecoChatClient = new Mock<IChatClient>();
        var chatClients = new Dictionary<string, IChatClient>
        {
            { "eco-model", ecoChatClient.Object }
        };
        var factory = new AgentFactory(chatClient.Object, NullLogger<AgentFactory>.Instance, chatClients);

        var tools = new List<ToolDefinition>
        {
            new() { Name = "Tool1", Description = "Desc1" },
            new() { Name = "Tool2", Description = "Desc2" }
        };

        ecoChatClient
            .Setup(c => c.GetResponseAsync(It.IsAny<IList<ChatMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API error"));

        var selector = new ToolSelector(factory, options, NullLogger<ToolSelector>.Instance);
        var result = await selector.SelectToolsAsync("find things", tools, 1);

        result.Should().HaveCount(1);
        result.Should().Contain(t => t.Name == "Tool1");
    }
}

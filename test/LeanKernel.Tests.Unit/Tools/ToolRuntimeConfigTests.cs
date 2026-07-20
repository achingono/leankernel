using FluentAssertions;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools;

using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class ToolRuntimeConfigTests
{
    [Fact]
    public void ToolSettings_Defaults_AreReasonable()
    {
        var settings = new ToolSettings();
        settings.Enabled.Should().BeTrue();
        settings.AllowedToolNames.Should().BeEmpty();
        settings.AllowedCategories.Should().BeEmpty();
        settings.DynamicHttp.AllowHosts.Should().BeEmpty();
        settings.BuiltIns.Calculation.Enabled.Should().BeTrue();
        settings.BuiltIns.Calculation.MaxInputItems.Should().Be(1000);
    }

    [Fact]
    public void WebSearchSettings_Defaults_AreReasonable()
    {
        var settings = new WebSearchSettings();
        settings.Provider.Should().Be("brave");
        settings.ApiKeyEnv.Should().Be("BRAVE_API_KEY");
        settings.AllowHosts.Should().Contain("api.search.brave.com");
        settings.AllowHosts.Should().Contain("api.duckduckgo.com");
    }

    [Fact]
    public void AgentSettings_ToolsProperty_DefaultsToEnabled()
    {
        var settings = new AgentSettings();
        settings.Tools.Should().NotBeNull();
        settings.Tools.Enabled.Should().BeTrue();
    }

    [Fact]
    public void OpenAISettings_ToolModel_DefaultsToEmpty()
    {
        var settings = new OpenAISettings();
        settings.ToolModel.Should().BeEmpty();
    }

    [Fact]
    public void ToolResult_ToString_SuccessReturnsOutput()
    {
        var result = new ToolResult { ToolName = "t", Success = true, Output = "hello" };
        result.ToString().Should().Be("hello");
    }

    [Fact]
    public void ToolResult_ToString_FailureReturnsError()
    {
        var result = new ToolResult { ToolName = "t", Success = false, Error = "something failed" };
        result.ToString().Should().Contain("something failed");
    }

    [Fact]
    public void ToolResult_ToString_NullOutput_ReturnsEmpty()
    {
        var result = new ToolResult { ToolName = "t", Success = true, Output = null };
        result.ToString().Should().BeEmpty();
    }

    [Fact]
    public void ToolDefinition_Parameters_DefaultsToEmptyList()
    {
        var def = new ToolDefinition();
        def.Parameters.Should().NotBeNull();
        def.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void ToolParameter_Defaults()
    {
        var p = new ToolParameter();
        p.Type.Should().Be("string");
        p.Required.Should().BeFalse();
    }
}
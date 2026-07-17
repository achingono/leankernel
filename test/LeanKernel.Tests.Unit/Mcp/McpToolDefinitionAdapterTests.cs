using System.Text.Json;
using FluentAssertions;
using LeanKernel.Logic.Mcp;
using LeanKernel.Logic.Tools;
using ModelContextProtocol.Protocol;
using Xunit;

namespace LeanKernel.Tests.Unit.Mcp;

public class McpToolDefinitionAdapterTests
{
    [Fact]
    public void ExtractParameters_WithEmptySchema_ReturnsEmpty()
    {
        var schema = new JsonElement();
        var result = McpToolDefinitionAdapter.ExtractParameters(schema);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractParameters_WithNonObjectSchema_ReturnsEmpty()
    {
        using var doc = JsonDocument.Parse("\"string\"");
        var result = McpToolDefinitionAdapter.ExtractParameters(doc.RootElement);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractParameters_WithNoProperties_ReturnsEmpty()
    {
        var json = """{"type": "object"}""";
        using var doc = JsonDocument.Parse(json);
        var result = McpToolDefinitionAdapter.ExtractParameters(doc.RootElement);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractParameters_WithProperties_ReturnsParsedParameters()
    {
        var json = """
        {
            "type": "object",
            "properties": {
                "url": { "type": "string", "description": "The URL to navigate to" },
                "timeout": { "type": "integer", "description": "Timeout in seconds" }
            },
            "required": ["url"]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = McpToolDefinitionAdapter.ExtractParameters(doc.RootElement);

        result.Should().HaveCount(2);

        result[0].Name.Should().Be("url");
        result[0].Type.Should().Be("string");
        result[0].Description.Should().Be("The URL to navigate to");
        result[0].Required.Should().BeTrue();

        result[1].Name.Should().Be("timeout");
        result[1].Type.Should().Be("integer");
        result[1].Description.Should().Be("Timeout in seconds");
        result[1].Required.Should().BeFalse();
    }

    [Fact]
    public void ExtractParameters_WhenTypeMissing_DefaultsToString()
    {
        var json = """
        {
            "type": "object",
            "properties": {
                "name": { "description": "A name parameter" }
            }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = McpToolDefinitionAdapter.ExtractParameters(doc.RootElement);

        result.Should().ContainSingle();
        result[0].Name.Should().Be("name");
        result[0].Type.Should().Be("string");
        result[0].Required.Should().BeFalse();
    }

    [Fact]
    public void ExtractParameters_WhenDescriptionMissing_DefaultsToEmpty()
    {
        var json = """
        {
            "type": "object",
            "properties": {
                "count": { "type": "integer" }
            }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = McpToolDefinitionAdapter.ExtractParameters(doc.RootElement);

        result.Should().ContainSingle();
        result[0].Description.Should().BeEmpty();
    }

    [Fact]
    public void FormatContentBlock_WithTextBlock_ReturnsText()
    {
        var block = new TextContentBlock { Text = "Hello, world!" };
        var result = McpToolDefinitionAdapter.FormatContentBlock(block);
        result.Should().Be("Hello, world!");
    }

    [Fact]
    public void FormatContentBlock_WithTextBlockNullText_ReturnsEmpty()
    {
        var block = new TextContentBlock { Text = null! };
        var result = McpToolDefinitionAdapter.FormatContentBlock(block);
        result.Should().Be(string.Empty);
    }

    [Fact]
    public void FormatContentBlock_WithImageBlock_ReturnsDataUri()
    {
        var block = ImageContentBlock.FromBytes(
            new ReadOnlyMemory<byte>([0, 1, 2]),
            "image/png");
        var result = McpToolDefinitionAdapter.FormatContentBlock(block);
        result.Should().StartWith("data:image/png;base64,");
    }
}

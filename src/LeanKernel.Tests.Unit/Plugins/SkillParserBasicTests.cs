using LeanKernel.Plugins.BuiltIn.Skills;
using Xunit;

namespace LeanKernel.Tests.Unit.Plugins;

public class SkillParserBasicTests
{
    [Fact]
    public void ParseSkillContent_WithMinimalFrontmatter_ReturnsDef()
    {
        // Arrange
        var content = """
            ---
            name: test
            description: "Test skill"
            metadata: {}
            runtime:
              type: cli
              command: test-cli
            operations:
              - id: test_op
                summary: "Test operation"
                invoke:
                  argv: [test]
                parameters: {}
            ---
            # Content
            """;

        var parser = new SkillParser();

        // Act
        var skill = parser.ParseSkillContent(content);

        // Assert
        Assert.NotNull(skill);
        Assert.Equal("test", skill.Name);
    }

    [Fact]
    public void ParseSkillContent_WithSimplefinFormat_ParsesOperations()
    {
        // Arrange
        var content = """
            ---
            name: simplefin
            description: "SimpleFin skill"
            metadata:
              emoji: "💸"
            runtime:
              type: cli
              command: simplefin-cli
              requires:
                bins:
                  - name: simplefin-cli
                    minVersion: "0.0.2"
            operations:
              - id: status
                summary: "Check status"
                invoke:
                  argv: [status]
                parameters:
                  type: object
                  properties: {}
            ---
            # SimpleFin
            """;

        var parser = new SkillParser();

        // Act
        var skill = parser.ParseSkillContent(content);

        // Assert
        Assert.NotNull(skill);
        Assert.Equal("simplefin", skill.Name);
        Assert.NotNull(skill.Runtime);
        Assert.Equal("cli", skill.Runtime.Type);
        Assert.Single(skill.Operations);
        Assert.Equal("status", skill.Operations[0].Id);
    }
}

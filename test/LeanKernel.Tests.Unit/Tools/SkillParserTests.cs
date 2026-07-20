using FluentAssertions;

using LeanKernel.Logic.Tools.Dynamic;

using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class SkillParserTests
{
    private readonly SkillParser _parser = new();

    private const string ValidSkillMd = """
        ---
        name: weather
        description: Weather lookups
        metadata:
          category: internet
        runtime:
          type: http
          baseUrl: https://api.example.com
          timeoutSeconds: 30
          auth:
            type: none
          egress:
            allowHosts:
              - api.example.com
        operations:
          - id: current
            summary: Get current weather
            invoke:
              httpMethod: GET
              httpPath: /v1/current/{city}
            parameters:
              city:
                type: string
                description: City name
                required: true
        ---

        # Weather Skill

        This skill provides weather lookups.
        """;

    [Fact]
    public void ParseContent_ValidSkill_ReturnsDefinition()
    {
        var result = _parser.ParseContent(ValidSkillMd);

        result.Should().NotBeNull();
        result!.Name.Should().Be("weather");
        result.Description.Should().Be("Weather lookups");
        result.Category.Should().Be("internet");
        result.Runtime.Type.Should().Be("http");
        result.Runtime.BaseUrl.Should().Be("https://api.example.com");
        result.Runtime.TimeoutSeconds.Should().Be(30);
        result.AllowedHosts.Should().Contain("api.example.com");
        result.Operations.Should().HaveCount(1);

        var op = result.Operations[0];
        op.Id.Should().Be("current");
        op.Summary.Should().Be("Get current weather");
        op.HttpMethod.Should().Be("GET");
        op.HttpPath.Should().Be("/v1/current/{city}");
        op.Parameters.Should().HaveCount(1);
        op.Parameters[0].Name.Should().Be("city");
        op.Parameters[0].Type.Should().Be("string");
        op.Parameters[0].Required.Should().BeTrue();
    }

    [Fact]
    public void ParseContent_NoFrontmatter_ReturnsNull()
    {
        var result = _parser.ParseContent("# Just markdown, no frontmatter");

        result.Should().BeNull();
    }

    [Fact]
    public void ParseContent_MissingName_ReturnsNull()
    {
        var content = """
            ---
            description: Missing name
            runtime:
              type: http
              baseUrl: https://api.example.com
            operations:
              - id: op1
                summary: Something
            ---
            """;

        var result = _parser.ParseContent(content);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseContent_CliRuntimeType_ReturnsNull()
    {
        var content = """
            ---
            name: cli_skill
            description: CLI tool
            runtime:
              type: cli
              command: /usr/bin/tool
            operations:
              - id: run
                summary: Run tool
            ---
            """;

        var result = _parser.ParseContent(content);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseContent_NoOperations_ReturnsNull()
    {
        var content = """
            ---
            name: no_ops
            description: No operations
            runtime:
              type: http
              baseUrl: https://api.example.com
            operations: []
            ---
            """;

        var result = _parser.ParseContent(content);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseContent_BearerAuth_ParsedCorrectly()
    {
        var content = """
            ---
            name: secure
            description: Secured tool
            runtime:
              type: http
              baseUrl: https://api.example.com
              auth:
                type: bearer
                secretRef: my_token
              egress:
                allowHosts:
                  - api.example.com
            operations:
              - id: get_data
                summary: Get data
                invoke:
                  httpMethod: GET
                  httpPath: /data
            ---
            """;

        var result = _parser.ParseContent(content);

        result.Should().NotBeNull();
        result!.Runtime.Auth.Type.Should().Be("bearer");
        result.Runtime.Auth.SecretRef.Should().Be("my_token");
    }

    [Fact]
    public void ParseContent_MultipleOperations_AllParsed()
    {
        var content = """
            ---
            name: multi
            description: Multiple ops
            runtime:
              type: http
              baseUrl: https://api.example.com
            operations:
              - id: get
                summary: Get something
                invoke:
                  httpMethod: GET
                  httpPath: /items
              - id: post
                summary: Post something
                invoke:
                  httpMethod: POST
                  httpPath: /items
            ---
            """;

        var result = _parser.ParseContent(content);

        result.Should().NotBeNull();
        result!.Operations.Should().HaveCount(2);
        result.Operations[0].Id.Should().Be("get");
        result.Operations[1].Id.Should().Be("post");
    }

    [Fact]
    public void Parse_NonexistentFile_ReturnsNull()
    {
        var result = _parser.Parse("/nonexistent/SKILL.md");

        result.Should().BeNull();
    }

    [Fact]
    public void Parse_ValidFile_ReturnsDefinition()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tmpDir);
        var filePath = Path.Combine(tmpDir, "SKILL.md");
        File.WriteAllText(filePath, ValidSkillMd);
        try
        {
            var result = _parser.Parse(filePath);

            result.Should().NotBeNull();
            result!.Name.Should().Be("weather");
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void ParseContent_DefaultTimeout_IsThirty()
    {
        var content = """
            ---
            name: minimal
            description: Minimal
            runtime:
              type: http
              baseUrl: https://api.example.com
            operations:
              - id: go
                summary: Go
            ---
            """;

        var result = _parser.ParseContent(content);

        result.Should().NotBeNull();
        result!.Runtime.TimeoutSeconds.Should().Be(30);
    }
}
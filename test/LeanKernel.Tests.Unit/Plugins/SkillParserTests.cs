using FluentAssertions;
using LeanKernel.Plugins.BuiltIn.Skills;

namespace LeanKernel.Tests.Unit.Plugins;

public class SkillParserTests
{
    [Fact]
    public void ParseContent_parses_a_complete_skill_definition_from_frontmatter()
    {
        var parser = new SkillParser();
        var skill = parser.ParseContent("""
---
name: sample_skill
description: "Sample skill"
metadata:
  category: demo
  enabled: true
runtime:
  type: cli
  command: sample-cli
  baseUrl: https://example.com
  timeoutSeconds: 45
  auth:
    type: bearer
    secretRef: skill-secret
  requires:
    bins:
      - name: sample-cli
        minVersion: "1.2.3"
        checksumSha256: abc123
  egress:
    allowHosts:
      - api.example.com
operations:
  - id: do_work
    summary: "Do work"
    invoke:
      argv: [run, --fast]
      flags:
        target: --target
      httpMethod: POST
      httpPath: /api/do-work
    parameters:
      type: object
      properties:
        target:
          type: string
---
body
""", "/tmp/skills/sample/SKILL.md");

        skill.Should().NotBeNull();
        skill!.Name.Should().Be("sample_skill");
        skill.Description.Should().Be("Sample skill");
        skill.SourcePath.Should().Be("/tmp/skills/sample/SKILL.md");
        skill.Metadata.Should().ContainKey("category").WhoseValue.Should().Be("demo");
        skill.Metadata.Should().ContainKey("enabled").WhoseValue!.ToString().Should().Be("true");

        skill.Runtime.Type.Should().Be("cli");
        skill.Runtime.Command.Should().Be("sample-cli");
        skill.Runtime.BaseUrl.Should().Be("https://example.com");
        skill.Runtime.TimeoutSeconds.Should().Be(45);
        skill.Runtime.Auth.Type.Should().Be("bearer");
        skill.Runtime.Auth.SecretRef.Should().Be("skill-secret");
        skill.Runtime.Requires.Bins.Should().ContainSingle();
        skill.Runtime.Requires.Bins[0].Name.Should().Be("sample-cli");
        skill.Runtime.Requires.Bins[0].MinVersion.Should().Be("1.2.3");
        skill.Runtime.Requires.Bins[0].ChecksumSha256.Should().Be("abc123");
        skill.Runtime.Egress.AllowHosts.Should().ContainSingle().Which.Should().Be("api.example.com");

        skill.Operations.Should().ContainSingle();
        var operation = skill.Operations[0];
        operation.Id.Should().Be("do_work");
        operation.Summary.Should().Be("Do work");
        operation.Invoke.Argv.Should().Equal("run", "--fast");
        operation.Invoke.Flags.Should().ContainKey("target").WhoseValue.Should().Be("--target");
        operation.Invoke.HttpMethod.Should().Be("POST");
        operation.Invoke.HttpPath.Should().Be("/api/do-work");
        operation.ParametersRaw.Should().NotBeNull();
        operation.ParametersRaw!.Should().ContainKey("type").WhoseValue.Should().Be("object");
    }

    [Fact]
    public void ParseContent_returns_null_when_frontmatter_is_missing()
    {
        var parser = new SkillParser();

        parser.ParseContent("No frontmatter here").Should().BeNull();
    }

    [Fact]
    public void ParseContent_returns_null_when_yaml_is_invalid()
    {
        var parser = new SkillParser();

        parser.ParseContent("""
---
name: broken_skill
description: Broken skill
operations:
  - id: broken
    summary: Broken
    invoke:
      argv: [run
---
""").Should().BeNull();
    }

    [Fact]
    public void Parse_returns_null_when_the_file_does_not_exist()
    {
        var parser = new SkillParser();

        parser.Parse(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "SKILL.md")).Should().BeNull();
    }
}

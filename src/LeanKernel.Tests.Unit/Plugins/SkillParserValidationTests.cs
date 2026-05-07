using LeanKernel.Plugins.BuiltIn.Skills;

namespace LeanKernel.Tests.Unit.Plugins;

/// <summary>
/// Tests for SkillParser validation, edge-case parsing paths, and error surfacing.
/// Increases coverage of ValidateRuntime, ValidateOperations, ValidateJsonSchema,
/// ValidateBinaryRequirements, ParseEgress, ParseInvoke (HTTP), ParseStringDict,
/// ExtractExamples, ParseSkillFileAsync, and IsValidSha256.
/// </summary>
public class SkillParserValidationTests
{
    private static SkillParser Parser() => new();

    // ── Runtime validation ──────────────────────────────────────────────────

    [Fact]
    public void ParseSkillContent_NoRuntimeBlock_ReportsValidationError()
    {
        var content = Frontmatter("""
            name: no_runtime
            description: "Missing runtime"
            operations:
              - id: op1
                summary: "op"
                invoke:
                  argv: [tool]
                parameters:
                  type: object
                  properties: {}
            """);

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.Contains(skill.ValidationErrors, e => e.Contains("runtime"));
    }

    [Fact]
    public void ParseSkillContent_HttpRuntimeMissingBaseUrl_ReportsError()
    {
        var content = Frontmatter("""
            name: http_skill
            description: "HTTP skill without baseUrl"
            runtime:
              type: http
              egress:
                allowHosts: ["api.example.com"]
            operations:
              - id: call
                summary: "call"
                invoke:
                  httpMethod: GET
                  httpPath: /ping
                parameters:
                  type: object
                  properties: {}
            """);

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.Contains(skill.ValidationErrors, e => e.Contains("baseUrl"));
    }

    [Fact]
    public void ParseSkillContent_HttpRuntimeMissingEgressAllowHosts_ReportsError()
    {
        var content = Frontmatter("""
            name: http_skill2
            description: "HTTP skill without egress"
            runtime:
              type: http
              baseUrl: "https://api.example.com"
            operations:
              - id: call
                summary: "call"
                invoke:
                  httpMethod: GET
                  httpPath: /ping
                parameters:
                  type: object
                  properties: {}
            """);

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.Contains(skill.ValidationErrors, e => e.Contains("allowHosts"));
    }

    [Fact]
    public void ParseSkillContent_CliRuntimeMissingCommand_ReportsError()
    {
        var content = Frontmatter("""
            name: cli_skill
            description: "CLI skill without command"
            runtime:
              type: cli
            operations:
              - id: run
                summary: "run"
                invoke:
                  argv: [tool]
                parameters:
                  type: object
                  properties: {}
            """);

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.Contains(skill.ValidationErrors, e => e.Contains("command"));
    }

    // ── Operation validation ────────────────────────────────────────────────

    [Fact]
    public void ParseSkillContent_ZeroOperations_ReportsError()
    {
        var content = Frontmatter("""
            name: empty_ops
            description: "No operations"
            runtime:
              type: cli
              command: tool
            operations: []
            """);

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.Contains(skill.ValidationErrors, e => e.Contains("operation"));
    }

    [Fact]
    public void ParseSkillContent_OperationWithoutInvoke_ReportsError()
    {
        var content = Frontmatter("""
            name: no_invoke
            description: "Operation without invoke"
            runtime:
              type: cli
              command: tool
            operations:
              - id: op1
                summary: "missing invoke"
                parameters:
                  type: object
                  properties: {}
            """);

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.Contains(skill.ValidationErrors, e => e.Contains("op1") && e.Contains("invoke"));
    }

    [Fact]
    public void ParseSkillContent_CliOperationWithEmptyArgv_ReportsError()
    {
        var content = Frontmatter("""
            name: empty_argv
            description: "CLI op with empty argv"
            runtime:
              type: cli
              command: tool
            operations:
              - id: do_thing
                summary: "does a thing"
                invoke:
                  argv: []
                parameters:
                  type: object
                  properties: {}
            """);

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.Contains(skill.ValidationErrors, e => e.Contains("empty argv"));
    }

    [Fact]
    public void ParseSkillContent_FlagNotInParameters_ReportsError()
    {
        var content = Frontmatter("""
            name: bad_flag
            description: "Flag not in parameters"
            runtime:
              type: cli
              command: tool
            operations:
              - id: run
                summary: "run"
                invoke:
                  argv: [tool, run]
                  flags:
                    unknown_flag: --unknown
                parameters:
                  type: object
                  properties:
                    known_param:
                      type: string
            """);

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.Contains(skill.ValidationErrors, e => e.Contains("unknown_flag"));
    }

    // ── JSON Schema validation ──────────────────────────────────────────────

    [Fact]
    public void ParseSkillContent_ParametersMissingType_ReportsError()
    {
        var content = Frontmatter("""
            name: no_type
            description: "Parameters without type"
            runtime:
              type: cli
              command: tool
            operations:
              - id: run
                summary: "run"
                invoke:
                  argv: [tool]
                parameters:
                  properties:
                    x:
                      type: string
            """);

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.Contains(skill.ValidationErrors, e => e.Contains("type"));
    }

    [Fact]
    public void ParseSkillContent_ParametersTypeNotObject_ReportsError()
    {
        var content = Frontmatter("""
            name: wrong_type
            description: "Parameters type is not object"
            runtime:
              type: cli
              command: tool
            operations:
              - id: run
                summary: "run"
                invoke:
                  argv: [tool]
                parameters:
                  type: array
                  items:
                    type: string
            """);

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.Contains(skill.ValidationErrors, e => e.Contains("object"));
    }

    // ── Binary requirements validation ──────────────────────────────────────

    [Fact]
    public void ParseSkillContent_BinaryWithValidSha256_NoChecksumError()
    {
        var sha = new string('a', 64);
        var yaml = string.Join('\n', [
            "name: bincheck",
            "description: \"Binary with valid checksum\"",
            "runtime:",
            "  type: cli",
            "  command: mytool",
            "  requires:",
            "    bins:",
            "      - name: mytool",
            "        minVersion: \"1.0\"",
            $"        checksumSha256: \"{sha}\"",
            "operations:",
            "  - id: run",
            "    summary: \"run\"",
            "    invoke:",
            "      argv: [mytool]",
            "    parameters:",
            "      type: object",
            "      properties: {}"
        ]);
        var content = $"---\n{yaml}\n---\n# Content\n";

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.DoesNotContain(skill.ValidationErrors, e => e.Contains("SHA256"));
    }

    [Fact]
    public void ParseSkillContent_BinaryWithInvalidSha256_ReportsChecksumError()
    {
        var content = Frontmatter("""
            name: badsha
            description: "Binary with invalid checksum"
            runtime:
              type: cli
              command: mytool
              requires:
                bins:
                  - name: mytool
                    checksumSha256: "not-a-valid-sha"
            operations:
              - id: run
                summary: "run"
                invoke:
                  argv: [mytool]
                parameters:
                  type: object
                  properties: {}
            """);

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.Contains(skill.ValidationErrors, e => e.Contains("SHA256") || e.Contains("checksum"));
    }

    // ── Egress / Auth / HTTP invoke parsing ─────────────────────────────────

    [Fact]
    public void ParseSkillContent_HttpRuntimeWithValidEgress_ParsesEgressHosts()
    {
        var content = Frontmatter("""
            name: http_valid
            description: "HTTP skill with full egress"
            runtime:
              type: http
              baseUrl: "https://api.example.com"
              egress:
                allowHosts: ["api.example.com", "auth.example.com"]
            operations:
              - id: call
                summary: "call"
                invoke:
                  httpMethod: POST
                  httpPath: /v1/data
                parameters:
                  type: object
                  properties: {}
            """);

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.NotNull(skill.Runtime?.Egress);
        Assert.Equal(2, skill.Runtime!.Egress.AllowHosts.Count);
        Assert.Contains("api.example.com", skill.Runtime.Egress.AllowHosts);
    }

    [Fact]
    public void ParseSkillContent_InvokeWithHttpMethodAndPath_ParsesCorrectly()
    {
        var content = Frontmatter("""
            name: http_invoke
            description: "HTTP invoke"
            runtime:
              type: http
              baseUrl: "https://api.example.com"
              egress:
                allowHosts: ["api.example.com"]
            operations:
              - id: fetch
                summary: "fetch data"
                invoke:
                  httpMethod: GET
                  httpPath: /v1/items/{id}
                parameters:
                  type: object
                  properties:
                    id:
                      type: string
            """);

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        var op = Assert.Single(skill.Operations);
        Assert.Equal("GET", op.Invoke?.HttpMethod);
        Assert.Equal("/v1/items/{id}", op.Invoke?.HttpPath);
    }

    [Fact]
    public void ParseSkillContent_InvokeWithFlags_ParsesFlags()
    {
        var content = Frontmatter("""
            name: flagged
            description: "CLI with flags"
            runtime:
              type: cli
              command: tool
            operations:
              - id: run
                summary: "run with flags"
                invoke:
                  argv: [tool, run]
                  flags:
                    verbose: --verbose
                    output: --output
                parameters:
                  type: object
                  properties:
                    verbose:
                      type: boolean
                    output:
                      type: string
            """);

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        var op = Assert.Single(skill.Operations);
        Assert.Equal("--verbose", op.Invoke?.Flags["verbose"]);
        Assert.Equal("--output", op.Invoke?.Flags["output"]);
    }

    [Fact]
    public void ParseSkillContent_WithAuthBlock_ParsesAuthType()
    {
        var content = Frontmatter("""
            name: authed
            description: "Skill with auth"
            runtime:
              type: cli
              command: mytool
              auth:
                type: bearer
                secretRef: MY_SECRET_TOKEN
            operations:
              - id: op
                summary: "op"
                invoke:
                  argv: [mytool]
                parameters:
                  type: object
                  properties: {}
            """);

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.Equal("bearer", skill.Runtime?.Auth.Type);
        Assert.Equal("MY_SECRET_TOKEN", skill.Runtime?.Auth.SecretRef);
    }

    // ── Examples extraction ─────────────────────────────────────────────────

    [Fact]
    public void ParseSkillContent_MarkdownWithCurlExample_ExtractsExample()
    {
        var content = """
            ---
            name: curl_example
            description: "Has curl example"
            runtime:
              type: cli
              command: tool
            operations:
              - id: run
                summary: "run"
                invoke:
                  argv: [tool]
                parameters:
                  type: object
                  properties: {}
            ---
            # Usage

            ```bash
            curl -X GET https://api.example.com/data
            ```
            """;

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.NotEmpty(skill.Examples);
        Assert.Contains("curl", skill.Examples[0].Code);
    }

    [Fact]
    public void ParseSkillContent_MarkdownWithNonCurlCodeBlock_DoesNotExtractExample()
    {
        var content = """
            ---
            name: no_curl
            description: "No curl example"
            runtime:
              type: cli
              command: tool
            operations:
              - id: run
                summary: "run"
                invoke:
                  argv: [tool]
                parameters:
                  type: object
                  properties: {}
            ---
            # Usage

            ```bash
            tool run --flag value
            ```
            """;

        var skill = Parser().ParseSkillContent(content);

        Assert.NotNull(skill);
        Assert.Empty(skill.Examples);
    }

    // ── ParseSkillFileAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ParseSkillFileAsync_NonExistentFile_ReturnsNull()
    {
        var result = await Parser().ParseSkillFileAsync("/nonexistent/SKILL.md");
        Assert.Null(result);
    }

    [Fact]
    public async Task ParseSkillFileAsync_ValidFile_ParsesSkill()
    {
        var tempFile = Path.GetTempFileName() + ".md";
        try
        {
            await File.WriteAllTextAsync(tempFile, Frontmatter("""
                name: file_skill
                description: "Loaded from file"
                runtime:
                  type: cli
                  command: tool
                operations:
                  - id: run
                    summary: "run"
                    invoke:
                      argv: [tool]
                    parameters:
                      type: object
                      properties: {}
                """));

            var skill = await Parser().ParseSkillFileAsync(tempFile);

            Assert.NotNull(skill);
            Assert.Equal("file_skill", skill.Name);
            Assert.Equal(tempFile, skill.SourcePath);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    // ── Content without frontmatter ─────────────────────────────────────────

    [Fact]
    public void ParseSkillContent_NoFrontmatterMarkers_ReturnsNull()
    {
        var result = Parser().ParseSkillContent("# Just a markdown file\n\nNo frontmatter here.");
        Assert.Null(result);
    }

    [Fact]
    public void ParseSkillContent_MissingNameOrDescription_ReturnsNull()
    {
        var content = Frontmatter("""
            description: "No name field"
            runtime:
              type: cli
              command: tool
            operations: []
            """);

        var result = Parser().ParseSkillContent(content);
        Assert.Null(result);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string Frontmatter(string yaml) =>
        $"---\n{yaml}\n---\n# Content\n";
}

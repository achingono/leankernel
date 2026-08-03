using FluentAssertions;

using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools.Dynamic;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class DynamicCliToolTests
{
    private static SkillDefinition MakeCliSkill(
        string name,
        string command,
        string? secretRef = null,
        int timeoutSeconds = 10,
        SkillOperation[]? operations = null) =>
        new()
        {
            Name = name,
            Description = $"{name} skill",
            Runtime = new SkillRuntimeConfig
            {
                Type = "cli",
                Command = command,
                TimeoutSeconds = timeoutSeconds,
                Auth = new SkillAuthConfig
                {
                    Type = secretRef is null ? "none" : "bearer",
                    SecretRef = secretRef
                }
            },
            Operations = operations ?? []
        };

    private static SkillOperation MakeCliOperation(
        string id,
        IReadOnlyList<string>? argv = null,
        IReadOnlyDictionary<string, string?>? flags = null,
        IReadOnlyList<SkillOperationParameter>? parameters = null) =>
        new()
        {
            Id = id,
            Summary = $"Test {id}",
            Argv = argv ?? [],
            Flags = new Dictionary<string, string?>(flags ?? new Dictionary<string, string?>()),
            Parameters = parameters ?? []
        };

    private static IServiceScopeFactory BuildScopeFactory(int maxOutputChars = 12_000)
    {
        var services = new ServiceCollection();
        services.Configure<AgentSettings>(opts =>
        {
            opts.Tools.DynamicCli.MaxOutputChars = maxOutputChars;
        });

        var sp = services.BuildServiceProvider();

        var mockFactory = new Mock<IServiceScopeFactory>();
        mockFactory.Setup(f => f.CreateScope())
            .Returns(() =>
            {
                var mockScope = new Mock<IServiceScope>();
                mockScope.Setup(s => s.ServiceProvider).Returns(sp);
                return mockScope.Object;
            });

        return mockFactory.Object;
    }

    private static string CreateExecutableScript(string body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "lk-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "lk-test-script.sh");
        File.WriteAllText(path, $"#!/bin/sh\n{body}\n");
        if (!OperatingSystem.IsWindows())
        {
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
        }

        return path;
    }

    private static string ScriptDir(string scriptPath) => Path.GetDirectoryName(scriptPath)!;

    [Fact]
    public void Create_ValidInputs_ReturnsToolDefinition()
    {
        var skill = MakeCliSkill("blog", "blog-cli");
        var op = MakeCliOperation("create_draft");
        var tool = DynamicCliTool.Create(skill, op, BuildScopeFactory());

        tool.Name.Should().Be("blog_create_draft");
        tool.Description.Should().Contain("blog");
        tool.Description.Should().Contain("Test create_draft");
    }

    [Fact]
    public void Create_NullArguments_Throw()
    {
        var act = () => DynamicCliTool.Create(null!, MakeCliOperation("op"), BuildScopeFactory());
        act.Should().Throw<ArgumentNullException>();

        var skill = MakeCliSkill("s", "binary");
        var act2 = () => DynamicCliTool.Create(skill, null!, BuildScopeFactory());
        act2.Should().Throw<ArgumentNullException>();

        var act3 = () => DynamicCliTool.Create(skill, MakeCliOperation("op"), null!);
        act3.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Handler_PassesArgvBeforeFlags_WithFlagValues()
    {
        var script = CreateExecutableScript("printf '%s\\n' \"$@\"");
        try
        {
            var op = MakeCliOperation(
                "create",
                argv: ["create_draft"],
                flags: new Dictionary<string, string?> { ["title"] = "--title", ["body"] = "--body" },
                parameters:
                [
                    new SkillOperationParameter { Name = "title", Type = "string" },
                    new SkillOperationParameter { Name = "body", Type = "string" }
                ]);
            var tool = DynamicCliTool.Create(MakeCliSkill("blog", script, operations: [op]), op, BuildScopeFactory());

            var result = await tool.Handler(
                new Dictionary<string, object?> { ["title"] = "Hello World", ["body"] = "Body text" },
                CancellationToken.None);

            result.Success.Should().BeTrue();
            var lines = result.Output!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            lines.Should().Equal("create_draft", "--title", "Hello World", "--body", "Body text");
        }
        finally
        {
            Directory.Delete(ScriptDir(script), true);
        }
    }

    [Fact]
    public async Task Handler_BooleanFlag_PresentWhenTrue_OmittedWhenFalse()
    {
        var script = CreateExecutableScript("printf '%s\\n' \"$@\"");
        try
        {
            var op = MakeCliOperation(
                "run",
                argv: ["run"],
                flags: new Dictionary<string, string?> { ["verbose"] = "--verbose" },
                parameters:
                [
                    new SkillOperationParameter { Name = "verbose", Type = "boolean" }
                ]);
            var tool = DynamicCliTool.Create(MakeCliSkill("s", script, operations: [op]), op, BuildScopeFactory());

            var present = await tool.Handler(new Dictionary<string, object?> { ["verbose"] = true }, CancellationToken.None);
            present.Success.Should().BeTrue();
            present.Output.Should().Be("run\n--verbose\n");

            var absent = await tool.Handler(new Dictionary<string, object?> { ["verbose"] = false }, CancellationToken.None);
            absent.Success.Should().BeTrue();
            absent.Output.Should().Be("run\n");

            var notSupplied = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);
            notSupplied.Success.Should().BeTrue();
            notSupplied.Output.Should().Be("run\n");
        }
        finally
        {
            Directory.Delete(ScriptDir(script), true);
        }
    }

    [Fact]
    public async Task Handler_NullFlagValue_PassesValueAsBarePositional()
    {
        var script = CreateExecutableScript("printf '%s\\n' \"$@\"");
        try
        {
            var op = MakeCliOperation(
                "setup",
                argv: ["setup"],
                flags: new Dictionary<string, string?> { ["token"] = null },
                parameters:
                [
                    new SkillOperationParameter { Name = "token", Type = "string" }
                ]);
            var tool = DynamicCliTool.Create(MakeCliSkill("sf", script, operations: [op]), op, BuildScopeFactory());

            var result = await tool.Handler(
                new Dictionary<string, object?> { ["token"] = "tok-value" },
                CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Output.Should().Be("setup\ntok-value\n");
        }
        finally
        {
            Directory.Delete(ScriptDir(script), true);
        }
    }

    [Fact]
    public async Task Handler_StdoutCaptured_OnExitCodeZero()
    {
        var script = CreateExecutableScript("echo 'hello stdout'");
        try
        {
            var op = MakeCliOperation("echo");
            var tool = DynamicCliTool.Create(MakeCliSkill("s", script, operations: [op]), op, BuildScopeFactory());

            var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Output.Should().Contain("hello stdout");
        }
        finally
        {
            Directory.Delete(ScriptDir(script), true);
        }
    }

    [Fact]
    public async Task Handler_StderrCaptured_OnNonZeroExit()
    {
        var script = CreateExecutableScript("echo 'boom happened' >&2\nexit 3");
        try
        {
            var op = MakeCliOperation("fail");
            var tool = DynamicCliTool.Create(MakeCliSkill("s", script, operations: [op]), op, BuildScopeFactory());

            var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("Exit code 3");
            result.Error.Should().Contain("boom happened");
        }
        finally
        {
            Directory.Delete(ScriptDir(script), true);
        }
    }

    [Fact]
    public async Task Handler_Timeout_KillsProcessTreeAndReturnsError()
    {
        var script = CreateExecutableScript("sleep 10\necho 'should not finish'");
        try
        {
            var skill = MakeCliSkill("s", script, timeoutSeconds: 1);
            var op = MakeCliOperation("hang");
            var tool = DynamicCliTool.Create(skill, op, BuildScopeFactory());

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);
            sw.Stop();

            result.Success.Should().BeFalse();
            result.Error.Should().Contain("timed out after 1s");
            sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));
        }
        finally
        {
            Directory.Delete(ScriptDir(script), true);
        }
    }

    [Fact]
    public async Task Handler_BearerSecret_InjectedAsChildEnvVar_NotAsArgument()
    {
        var script = CreateExecutableScript("printf 'ARGS[%s]\\n' \"$@\"\nprintf 'token=%s\\n' \"$SKILL__TEST_SECRET\"");
        try
        {
            Environment.SetEnvironmentVariable("SKILL__TEST_SECRET", "s3cr3t-value");
            try
            {
                var skill = MakeCliSkill("s", script, secretRef: "test-secret");
                var op = MakeCliOperation("run");
                var tool = DynamicCliTool.Create(skill, op, BuildScopeFactory());

                var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

                result.Success.Should().BeTrue();
                result.Output.Should().Contain("token=s3cr3t-value");
                result.Output.Should().Be("ARGS[]\ntoken=s3cr3t-value\n",
                    "the secret must be injected via the process environment, never as a CLI argument");
            }
            finally
            {
                Environment.SetEnvironmentVariable("SKILL__TEST_SECRET", null);
            }
        }
        finally
        {
            Directory.Delete(ScriptDir(script), true);
        }
    }

    [Fact]
    public async Task Handler_BearerSecretResolutionFailure_ReturnsError()
    {
        var skill = MakeCliSkill("s", "some-binary", secretRef: "nonexistent-secret-xyz");
        var op = MakeCliOperation("run");
        var tool = DynamicCliTool.Create(skill, op, BuildScopeFactory());

        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("nonexistent-secret-xyz");
    }

    [Fact]
    public async Task Handler_MissingBinary_ReturnsError()
    {
        var skill = MakeCliSkill("s", "/nonexistent/lk-xyz-binary-123");
        var op = MakeCliOperation("run");
        var tool = DynamicCliTool.Create(skill, op, BuildScopeFactory());

        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Failed to start");
    }

    [Fact]
    public async Task Handler_OutputTruncated_AtConfiguredMaxLength()
    {
        var script = CreateExecutableScript("echo '1234567890abcdef'");
        try
        {
            var op = MakeCliOperation("echo");
            var tool = DynamicCliTool.Create(
                MakeCliSkill("s", script, operations: [op]), op, BuildScopeFactory(maxOutputChars: 10));

            var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            result.Output.Should().Be("1234567890");
        }
        finally
        {
            Directory.Delete(ScriptDir(script), true);
        }
    }
}

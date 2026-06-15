using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Models;
using LeanKernel.Plugins.BuiltIn.Skills;
using Microsoft.Extensions.Http;
using Moq;

namespace LeanKernel.Tests.Unit.Tools;

public class DynamicSkillToolTests
{
    [Fact]
    public async Task ExecuteCliAsync_emits_presence_flag_for_true_boolean_values()
    {
        var tool = CreateCaptureTool();

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["slug"] = "my-post",
            ["approved_by_user"] = true
        }, CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        var lines = SplitLines(result.Output);

        lines.Should().ContainInOrder(
            "prepare_publish",
            "--slug",
            "my-post",
            "--approved-by-user");
        lines.Should().NotContain("True");
        lines.Should().NotContain("true");
    }

    [Fact]
    public async Task ExecuteCliAsync_omits_false_boolean_values()
    {
        var tool = CreateCaptureTool();

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["slug"] = "my-post",
            ["approved_by_user"] = false
        }, CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        var lines = SplitLines(result.Output);

        lines.Should().ContainInOrder(
            "prepare_publish",
            "--slug",
            "my-post");
        lines.Should().NotContain("--approved-by-user");
        lines.Should().NotContain("False");
        lines.Should().NotContain("false");
    }

    [Fact]
    public async Task ExecuteCliAsync_handles_json_element_boolean_values()
    {
        var tool = CreateCaptureTool();

        using var json = JsonDocument.Parse("true");
        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["slug"] = "my-post",
            ["approved_by_user"] = json.RootElement.Clone()
        }, CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        var lines = SplitLines(result.Output);

        lines.Should().ContainInOrder(
            "prepare_publish",
            "--slug",
            "my-post",
            "--approved-by-user");
        lines.Should().NotContain("True");
        lines.Should().NotContain("true");
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("False", false)]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task ExecuteCliAsync_handles_boolean_like_scalar_values(object value, bool expectedFlag)
    {
        var tool = CreateCaptureTool();

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["slug"] = "my-post",
            ["approved_by_user"] = value
        }, CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        var lines = SplitLines(result.Output);

        lines.Should().Contain("prepare_publish");
        lines.Should().Contain("--slug");
        lines.Should().Contain("my-post");

        if (expectedFlag)
        {
            lines.Should().Contain("--approved-by-user");
        }
        else
        {
            lines.Should().NotContain("--approved-by-user");
        }
    }

    private static ToolDefinition CreateCaptureTool()
    {
        var root = Path.Combine(Path.GetTempPath(), "leankernel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var skill = new SkillDefinition
        {
            Name = "blog",
            Description = "Blog skill",
            Runtime = new SkillRuntimeConfig
            {
                Type = "cli",
                Command = OperatingSystem.IsWindows() ? "cmd.exe" : CreateScript(root),
                TimeoutSeconds = 5,
            },
            Operations =
            [
                new SkillOperation
                {
                    Id = "prepare_publish",
                    Summary = "Prepare publish",
                    Invoke = new SkillInvokeConfig
                    {
                        Argv = OperatingSystem.IsWindows()
                            ? ["/d", "/s", "/c", CreateScript(root), "prepare_publish"]
                            : ["prepare_publish"],
                        Flags = new Dictionary<string, string>
                        {
                            ["slug"] = "--slug",
                            ["approved_by_user"] = "--approved-by-user"
                        }
                    }
                }
            ]
        };

        return DynamicSkillTool.CreateTool(skill, skill.Operations[0], Mock.Of<IHttpClientFactory>());
    }

    private static string CreateScript(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            var scriptPath = Path.Combine(root, "capture-args.cmd");
            File.WriteAllText(scriptPath, "@echo off\r\nsetlocal enabledelayedexpansion\r\n:loop\r\nif \"%~1\"==\"\" goto end\r\necho %~1\r\nshift\r\ngoto loop\r\n:end\r\n");
            return scriptPath;
        }

        var unixScriptPath = Path.Combine(root, "capture-args.sh");
        File.WriteAllText(unixScriptPath, "#!/usr/bin/env bash\nfor arg in \"$@\"; do\n  printf '%s\\n' \"$arg\"\ndone\n");
        File.SetUnixFileMode(unixScriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return unixScriptPath;
    }

    private static string[] SplitLines(string? output)
    {
        return (output ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

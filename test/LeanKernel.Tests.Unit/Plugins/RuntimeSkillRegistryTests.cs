using FluentAssertions;
using LeanKernel.Plugins.BuiltIn.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeanKernel.Tests.Unit.Plugins;

public class RuntimeSkillRegistryTests
{
    [Fact]
    public void GetSkill_returns_null_when_registry_is_empty()
    {
        var registry = CreateRegistry([]);

        registry.GetSkill("nonexistent").Should().BeNull();
    }

    [Fact]
    public void LoadAll_loads_valid_skill_into_registry()
    {
        using var dir = new TempDirectory();
        WriteSkillFile(dir.Path, "my_skill", "http");

        var registry = CreateRegistry([dir.Path]);
        registry.LoadAll();

        var skill = registry.GetSkill("my_skill");
        skill.Should().NotBeNull();
        skill!.Description.Should().Be("Test skill");
        skill.Operations.Should().ContainSingle(o => o.Id == "do_stuff");
    }

    [Fact]
    public void Skills_returns_all_loaded_skills()
    {
        using var dir = new TempDirectory();
        WriteSkillFile(Path.Combine(dir.Path, "a"), "alpha", "http");
        WriteSkillFile(Path.Combine(dir.Path, "b"), "beta", "http");

        var registry = CreateRegistry([dir.Path]);
        registry.LoadAll();

        registry.Skills.Should().HaveCount(2);
        registry.Skills.Keys.Should().Contain(["alpha", "beta"]);
    }

    [Fact]
    public void LoadAll_with_duplicate_name_last_one_wins()
    {
        using var dir = new TempDirectory();
        WriteSkillFile(Path.Combine(dir.Path, "a"), "dup_skill", "http", operationId: "from_a");
        WriteSkillFile(Path.Combine(dir.Path, "b"), "dup_skill", "http", operationId: "from_b");

        var registry = CreateRegistry([dir.Path]);
        registry.LoadAll();

        var skill = registry.GetSkill("dup_skill");
        skill.Should().NotBeNull();
        skill!.Operations.Should().ContainSingle(o => o.Id == "from_b");
    }

    [Fact]
    public void LoadAll_quarantines_invalid_skill_file()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "SKILL.md"), "not valid frontmatter");

        var registry = CreateRegistry([dir.Path]);
        registry.LoadAll();

        registry.Skills.Should().BeEmpty();
        registry.Quarantined.Should().ContainSingle();
    }

    [Fact]
    public void LoadAll_handles_nonexistent_base_path_gracefully()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing");

        var registry = CreateRegistry([missing]);
        registry.LoadAll();

        registry.Skills.Should().BeEmpty();
        registry.Quarantined.Should().BeEmpty();
    }

    [Fact]
    public void LoadAll_scans_skills_from_multiple_base_paths()
    {
        using var dir1 = new TempDirectory();
        using var dir2 = new TempDirectory();
        WriteSkillFile(dir1.Path, "skill_one", "cli", command: "tool-a");
        WriteSkillFile(dir2.Path, "skill_two", "cli", command: "tool-b");

        var registry = CreateRegistry([dir1.Path, dir2.Path]);
        registry.LoadAll();

        registry.Skills.Should().HaveCount(2);
        registry.GetSkill("skill_one").Should().NotBeNull();
        registry.GetSkill("skill_two").Should().NotBeNull();
    }

    [Fact]
    public void LoadAll_clears_previous_skills_on_reload()
    {
        using var dir = new TempDirectory();
        var batch1 = Path.Combine(dir.Path, "batch1");
        WriteSkillFile(batch1, "first", "http");

        var registry = CreateRegistry([dir.Path]);
        registry.LoadAll();
        registry.Skills.Should().ContainKey("first");

        Directory.Delete(batch1, recursive: true);
        WriteSkillFile(Path.Combine(dir.Path, "batch2"), "second", "http");

        registry.LoadAll();
        registry.Skills.Should().ContainKey("second");
        registry.Skills.Should().NotContainKey("first");
    }

    [Fact]
    public void Quarantined_returns_empty_when_all_skills_load_successfully()
    {
        using var dir = new TempDirectory();
        WriteSkillFile(dir.Path, "good", "http");

        var registry = CreateRegistry([dir.Path]);
        registry.LoadAll();

        registry.Quarantined.Should().BeEmpty();
    }

    [Fact]
    public void LoadAll_quarantines_skills_with_missing_required_bins()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Path);
        File.WriteAllText(Path.Combine(dir.Path, "SKILL.md"),
            """
            ---
            name: bin_guarded
            description: Bin constrained skill
            runtime:
              type: cli
              command: dotnet
              requires:
                bins:
                  - name: missing-binary-for-test
            operations:
              - id: do_stuff
                summary: Does stuff
            ---
            """);

        var registry = CreateRegistry([dir.Path]);
        registry.LoadAll();

        registry.Skills.Should().BeEmpty();
        registry.Quarantined.Should().ContainSingle();
    }

    private static RuntimeSkillRegistry CreateRegistry(string[] basePaths)
    {
        return new RuntimeSkillRegistry(
            basePaths,
            new SkillParser(),
            NullLogger<RuntimeSkillRegistry>.Instance);
    }

    private static void WriteSkillFile(
        string parentDir,
        string name,
        string runtimeType,
        string? operationId = null,
        string? command = null)
    {
        Directory.CreateDirectory(parentDir);

        var yaml = command is not null
            ? $"""
               ---
               name: {name}
               description: Test skill
               runtime:
                 type: {runtimeType}
                 command: {command}
               operations:
                 - id: {operationId ?? "do_stuff"}
                   summary: Does stuff
               ---
               """
            : $"""
               ---
               name: {name}
               description: Test skill
               runtime:
                 type: {runtimeType}
                 egress:
                   allowHosts:
                     - example.com
               operations:
                 - id: {operationId ?? "do_stuff"}
                   summary: Does stuff
               ---
               """;

        File.WriteAllText(Path.Combine(parentDir, "SKILL.md"), yaml);
    }
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "leankernel-tests",
        Guid.NewGuid().ToString("N"));

    public TempDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}

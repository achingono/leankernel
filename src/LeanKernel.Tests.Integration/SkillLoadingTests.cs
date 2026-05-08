using LeanKernel.Plugins.BuiltIn.Skills;
using Xunit;
using System.Text.Json;

namespace LeanKernel.Tests.Integration;

public class SkillLoadingTests
{
    private readonly SkillParser _parser;

    public SkillLoadingTests()
    {
        _parser = new SkillParser();
    }

    private string GetSkillPath(string skillName)
    {
        var repoRoot = FindRepositoryRoot();
        return Path.Combine(repoRoot, "data", "skills", skillName, "SKILL.md");
    }

    private string FindRepositoryRoot()
    {
        // Try absolute path first (known location)
        var knownPaths = new[]
        {
            "/Users/achingono/source/repos/LeanKernel",
            "/home/achingono/source/repos/LeanKernel",
            Environment.GetEnvironmentVariable("LEANKERNEL_ROOT") ?? ""
        };

        foreach (var knownPath in knownPaths)
        {
            if (!string.IsNullOrEmpty(knownPath) && Directory.Exists(knownPath) && 
                File.Exists(Path.Combine(knownPath, ".git", "config")))
            {
                return knownPath;
            }
        }

        // Fall back to searching from current directory
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, ".git", "config")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find repository root");
    }

    [Theory]
    [InlineData("simplefin")]
    [InlineData("ms-todo")]
    [InlineData("doughray")]
    [InlineData("emanate")]
    [InlineData("screenshot-ocr")]
    public async Task LoadSkill_AllExistingSkills_ParseSuccessfully(string skillName)
    {
        // Arrange
        var skillPath = GetSkillPath(skillName);
        Assert.True(File.Exists(skillPath), $"Skill file not found: {skillPath}");

        // Act
        var skill = await _parser.ParseSkillFileAsync(skillPath);

        // Assert - provide better error message if null
        if (skill == null)
        {
            var content = await File.ReadAllTextAsync(skillPath);
            throw new InvalidOperationException($"Parser returned null for {skillName}.\nFirst 200 chars:\n{content[..Math.Min(200, content.Length)]}");
        }
        
        Assert.NotNull(skill);
        Assert.NotEmpty(skill.Name);
        Assert.NotEmpty(skill.Description);
        Assert.NotNull(skill.Runtime);
        Assert.Empty(skill.ValidationErrors);
    }

    [Fact]
    public async Task LoadSkill_Simplefin_HasRequiredOperations()
    {
        // Arrange
        var skillPath = GetSkillPath("simplefin");

        // Act
        var skill = await _parser.ParseSkillFileAsync(skillPath);

        // Assert
        Assert.NotNull(skill);
        var operationIds = skill.Operations.Select(op => op.Id).ToList();
        
        Assert.Contains("status", operationIds);
        Assert.Contains("list_accounts", operationIds);
        Assert.Contains("list_transactions", operationIds);
    }

    [Fact]
    public async Task LoadSkill_Simplefin_StatusOperationHasCorrectSchema()
    {
        // Arrange
        var skillPath = GetSkillPath("simplefin");

        // Act
        var skill = await _parser.ParseSkillFileAsync(skillPath);
        var statusOp = skill?.Operations.FirstOrDefault(op => op.Id == "status");

        // Assert
        Assert.NotNull(statusOp);
        Assert.Equal("Show CLI configuration and link status.", statusOp.Summary);
        Assert.NotNull(statusOp.Invoke?.Argv);
        Assert.Contains("status", statusOp.Invoke.Argv);
    }

    [Fact]
    public async Task LoadSkill_Simplefin_ListTransactionsHasFlags()
    {
        // Arrange
        var skillPath = GetSkillPath("simplefin");

        // Act
        var skill = await _parser.ParseSkillFileAsync(skillPath);
        var listTransOp = skill?.Operations.FirstOrDefault(op => op.Id == "list_transactions");

        // Assert
        Assert.NotNull(listTransOp);
        Assert.NotNull(listTransOp.Invoke?.Flags);
        Assert.Contains("accountId", listTransOp.Invoke.Flags.Keys);
        Assert.Contains("startDate", listTransOp.Invoke.Flags.Keys);
        Assert.Contains("endDate", listTransOp.Invoke.Flags.Keys);
        Assert.Equal("--account-id", listTransOp.Invoke.Flags["accountId"]);
        Assert.Equal("--start-date", listTransOp.Invoke.Flags["startDate"]);
    }

    [Fact]
    public async Task LoadSkill_MsTodo_HasRequiredOperations()
    {
        // Arrange
        var skillPath = GetSkillPath("ms-todo");

        // Act
        var skill = await _parser.ParseSkillFileAsync(skillPath);

        // Assert
        Assert.NotNull(skill);
        Assert.NotEmpty(skill.Operations);
        var operationIds = skill.Operations.Select(op => op.Id).ToList();
        
        // Verify at least common todo operations exist
        Assert.True(operationIds.Count > 0);
    }

    [Fact]
    public async Task LoadSkill_ScreenshotOcr_RequiresPaddleocr()
    {
        // Arrange
        var skillPath = GetSkillPath("screenshot-ocr");

        // Act
        var skill = await _parser.ParseSkillFileAsync(skillPath);

        // Assert
        Assert.NotNull(skill);
        Assert.NotNull(skill.Runtime);
        Assert.NotNull(skill.Runtime.Requires);
        
        var paddleocrReq = skill.Runtime.Requires.Bins.FirstOrDefault(b => b.Name == "paddleocr");
        Assert.NotNull(paddleocrReq);
    }

    [Fact]
    public async Task LoadSkill_AllSkills_HaveNonEmptyMetadata()
    {
        // Arrange
        var skillNames = new[] { "simplefin", "ms-todo", "doughray", "emanate", "screenshot-ocr" };

        foreach (var skillName in skillNames)
        {
            // Act
            var skillPath = GetSkillPath(skillName);
            var skill = await _parser.ParseSkillFileAsync(skillPath);

            // Assert
            Assert.NotNull(skill);
            Assert.NotEmpty(skill.Metadata);
            Assert.True(skill.Metadata.ContainsKey("emoji") || skill.Metadata.ContainsKey("category"));
        }
    }

    [Fact]
    public async Task LoadSkill_AllSkills_HaveOperationsWithParameters()
    {
        // Arrange
        var skillNames = new[] { "simplefin", "ms-todo", "doughray", "emanate", "screenshot-ocr" };

        foreach (var skillName in skillNames)
        {
            // Act
            var skillPath = GetSkillPath(skillName);
            var skill = await _parser.ParseSkillFileAsync(skillPath);

            // Assert
            Assert.NotNull(skill);
            Assert.NotEmpty(skill.Operations);
            
            foreach (var op in skill.Operations)
            {
                Assert.NotEmpty(op.Id);
                Assert.NotEmpty(op.Summary);
                Assert.NotNull(op.Invoke);
                Assert.NotNull(op.Parameters);
            }
        }
    }

    [Fact]
    public async Task LoadSkill_CliSkills_HaveCommand()
    {
        // Arrange
        var cliSkills = new[] { "simplefin", "ms-todo" };

        foreach (var skillName in cliSkills)
        {
            // Act
            var skillPath = GetSkillPath(skillName);
            var skill = await _parser.ParseSkillFileAsync(skillPath);

            // Assert
            Assert.NotNull(skill);
            Assert.NotNull(skill.Runtime);
            Assert.Equal("cli", skill.Runtime.Type);
            var command = skill.Runtime.Command;
            Assert.False(string.IsNullOrWhiteSpace(command));
        }
    }

    [Fact]
    public async Task LoadSkill_HttpSkills_HaveBaseUrlAndEgress()
    {
        // Arrange - check doughray
        var skillPath = GetSkillPath("doughray");
        
        if (!File.Exists(skillPath))
            return;

        // Act
        var skill = await _parser.ParseSkillFileAsync(skillPath);

        // Assert
        Assert.NotNull(skill);
        Assert.NotNull(skill.Runtime);
        
        if (skill.Runtime.Type == "http" || skill.Runtime.Type == "composite")
        {
            if (!string.IsNullOrEmpty(skill.Runtime.BaseUrl))
            {
                Assert.True(Uri.TryCreate(skill.Runtime.BaseUrl, UriKind.Absolute, out _));
                Assert.NotNull(skill.Runtime.Egress);
            }
        }
    }

    [Fact]
    public async Task LoadSkill_AllOperations_HaveValidJsonSchema()
    {
        // Arrange
        var skillNames = new[] { "simplefin", "ms-todo", "doughray", "emanate", "screenshot-ocr" };

        foreach (var skillName in skillNames)
        {
            // Act
            var skillPath = GetSkillPath(skillName);
            var skill = await _parser.ParseSkillFileAsync(skillPath);

            // Assert
            Assert.NotNull(skill);
            foreach (var op in skill.Operations)
            {
                Assert.NotNull(op.Parameters);
                Assert.NotEmpty(op.Parameters);
                
                // Try to parse as JSON to ensure it's valid
                Exception? ex = null;
                try
                {
                    var parametersJson = System.Text.Json.JsonSerializer.Serialize(op.Parameters);
                    using var doc = JsonDocument.Parse(parametersJson);
                }
                catch (Exception e)
                {
                    ex = e;
                }
                
                Assert.Null(ex);
            }
        }
    }

    [Fact]
    public async Task LoadSkill_BinaryRequirements_HaveVersions()
    {
        // Arrange
        var skillPath = GetSkillPath("simplefin");

        // Act
        var skill = await _parser.ParseSkillFileAsync(skillPath);

        // Assert
        Assert.NotNull(skill);
        Assert.NotNull(skill.Runtime?.Requires);
        Assert.NotEmpty(skill.Runtime.Requires.Bins);
        
        foreach (var bin in skill.Runtime.Requires.Bins)
        {
            Assert.NotEmpty(bin.Name);
            Assert.NotNull(bin.MinVersion);
            Assert.NotEmpty(bin.MinVersion);
        }
    }

    [Fact]
    public async Task ValidateSkill_Simplefin_HasNoValidationErrors()
    {
        // Arrange
        var skillPath = GetSkillPath("simplefin");

        // Act
        var skill = await _parser.ParseSkillFileAsync(skillPath);

        // Assert
        Assert.NotNull(skill);
        Assert.Empty(skill.ValidationErrors);
    }

    [Fact]
    public async Task ValidateSkill_AllSkills_HaveNoValidationErrors()
    {
        // Arrange
        var skillNames = new[] { "simplefin", "ms-todo", "doughray", "emanate", "screenshot-ocr" };

        foreach (var skillName in skillNames)
        {
            // Act
            var skillPath = GetSkillPath(skillName);
            var skill = await _parser.ParseSkillFileAsync(skillPath);

            // Assert
            Assert.NotNull(skill);
            Assert.Empty(skill.ValidationErrors);
        }
    }
}

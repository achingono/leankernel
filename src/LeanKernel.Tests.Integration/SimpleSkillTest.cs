using LeanKernel.Plugins.BuiltIn.Skills;
using Xunit;

namespace LeanKernel.Tests.Integration;

public class SimpleSkillTest
{
    [Fact]
    public async Task TestSimplefinSkillLoads()
    {
        var parser = new SkillParser();
        var path = Path.Combine(FindRepositoryRoot(), "data", "skills", "simplefin", "SKILL.md");
        
        Assert.True(File.Exists(path), $"File not found: {path}");
        
        try
        {
            // Read file directly to verify frontmatter
            var content = await File.ReadAllTextAsync(path);
            Assert.NotEmpty(content);
            Assert.StartsWith("---", content);
            
            var skill = await parser.ParseSkillFileAsync(path);
            
            if (skill == null)
            {
                var lines = content.Split('\n').Take(30).ToList();
                throw new InvalidOperationException($"Parser returned null.\nFirst 30 lines:\n{string.Join("\n", lines)}");
            }
            
            Assert.NotNull(skill);
            Assert.Equal("simplefin", skill.Name);
            Assert.NotNull(skill.Runtime);
            Assert.Equal("cli", skill.Runtime.Type);
            Assert.NotEmpty(skill.Operations);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Parser exception: {ex.Message}\n{ex.StackTrace}", ex);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".git", "config")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}

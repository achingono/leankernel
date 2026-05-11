using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Tests.Unit.Archivist;

public sealed class EngagementFileMaintenanceServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"LeanKernel-engagement-maintenance-{Guid.NewGuid():N}");

    [Fact]
    public async Task MaintainAsync_PdfWithoutExtractor_ReturnsExplicitError()
    {
        Directory.CreateDirectory(Path.Combine(_root, "documents"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "documents", "profile.pdf"), [1, 2, 3]);
        var service = CreateService(textExtractor: null);

        var result = await service.MaintainAsync(
            new EngagementFileMaintenanceRequest("Read `profile.pdf` and update engagement files.", ["profile.pdf"], []),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("Text extraction unavailable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.SourceFilesFound, path => path.EndsWith("profile.pdf", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(result.SourceFilesRead);
    }

    [Fact]
    public async Task MaintainAsync_CleansPlaceholdersAndWritesSourceBackedExcerpts()
    {
        Directory.CreateDirectory(Path.Combine(_root, "documents"));
        Directory.CreateDirectory(Path.Combine(_root, "agents", "main"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "documents", "profile.pdf"), [1, 2, 3]);
        await File.WriteAllTextAsync(Path.Combine(_root, "agents", "main", "USER.md"), """
            # USER.md - User Profile & Preferences

            ## User Profile

            - Your role or title (auto-detected from wiki)
            - Preferred response length: concise/moderate/detailed
            needed
            """);
        var extractor = new FakeExtractor("Director of platform engineering with strengths in strategic leadership and developer experience.");
        var service = CreateService(extractor);

        var result = await service.MaintainAsync(
            new EngagementFileMaintenanceRequest("Read `profile.pdf` and update USER.md.", ["profile.pdf"], ["USER.md"]),
            CancellationToken.None);

        var userContent = await File.ReadAllTextAsync(Path.Combine(_root, "agents", "main", "USER.md"));
        Assert.True(result.Success);
        Assert.Contains(result.SourceFilesRead, path => path.EndsWith("profile.pdf", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Source-Backed Document Insights", userContent);
        Assert.Contains("Director of platform engineering", userContent);
        Assert.DoesNotContain("Your role or title", userContent);
        Assert.DoesNotContain("concise/moderate/detailed", userContent);
        Assert.DoesNotContain("\nneeded\n", userContent);
    }

    [Fact]
    public async Task MaintainAsync_ExactNameSearch_ReadsSourceDocumentOutsideAgentsDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_root, "documents", "nested"));
        Directory.CreateDirectory(Path.Combine(_root, "agents", "main"));
        await File.WriteAllTextAsync(
            Path.Combine(_root, "documents", "nested", "e2e-profile.md"),
            "Verified source profile says Alfero leads platform engineering and developer experience programs.");
        await File.WriteAllTextAsync(
            Path.Combine(_root, "agents", "main", "e2e-profile.md"),
            "This generated agent artifact must not be treated as the source document.");
        var service = CreateService(textExtractor: null);

        var result = await service.MaintainAsync(
            new EngagementFileMaintenanceRequest("Read e2e-profile.md and update USER.md.", ["e2e-profile.md"], ["USER.md"]),
            CancellationToken.None);

        var sourcePath = Assert.Single(result.SourceFilesRead);
        Assert.Contains(Path.Combine("documents", "nested", "e2e-profile.md"), sourcePath);
        Assert.DoesNotContain(Path.Combine("agents", "main"), sourcePath);
        Assert.Contains(result.SourceExcerpts, excerpt => excerpt.Contains("platform engineering", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MaintainAsync_DocxWithExtractor_ChangesAndVerifiesAllEngagementFiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "documents"));
        Directory.CreateDirectory(Path.Combine(_root, "agents", "main"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "documents", "strengths.docx"), [4, 5, 6]);
        await File.WriteAllTextAsync(Path.Combine(_root, "agents", "main", "AGENTS.md"), "# AGENTS.md\n\n## Agent Personality\n\n**Tone:** default\n");
        await File.WriteAllTextAsync(Path.Combine(_root, "agents", "main", "SELF.md"), "# SELF.md\n");
        await File.WriteAllTextAsync(Path.Combine(_root, "agents", "main", "USER.md"), "# USER.md\n");
        var extractor = new FakeExtractor("Alfero has strategic leadership strengths, agentic AI expertise, and developer experience depth.");
        var service = CreateService(extractor);

        var result = await service.MaintainAsync(
            new EngagementFileMaintenanceRequest(
                "Agent name: Kem. Be direct, cover blind spots, use America/Toronto, and update AGENTS.md, SELF.md, USER.md from strengths.docx.",
                ["strengths.docx"],
                ["AGENTS.md", "SELF.md", "USER.md"]),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.SourceFilesRead, path => path.EndsWith("strengths.docx", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, result.VerifiedFiles.Count);
        Assert.Equal(3, result.ChangedFiles.Count);
        Assert.Contains(result.ChangedFiles, path => path.EndsWith("AGENTS.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.ChangedFiles, path => path.EndsWith("SELF.md", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.ChangedFiles, path => path.EndsWith("USER.md", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MaintainAsync_SignalStyleRequest_WritesOnlyVerifiedSourceBackedFacts()
    {
        Directory.CreateDirectory(Path.Combine(_root, "documents"));
        Directory.CreateDirectory(Path.Combine(_root, "agents", "main"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "documents", "profile.pdf"), [7, 8, 9]);
        await File.WriteAllTextAsync(Path.Combine(_root, "agents", "main", "USER.md"), """
            # USER.md - User Profile & Preferences

            ## User Profile

            - Preferred response length: concise/moderate/detailed
            - Role: needed
            """);
        var extractor = new FakeExtractor("Alfero is a director of platform engineering with strategic leadership, automation, and agentic AI strengths.");
        var service = CreateService(extractor);

        var result = await service.MaintainAsync(
            new EngagementFileMaintenanceRequest(
                "Read profile.pdf and use the insights to update the engagement files AGENTS.md, SELF.md, and USER.md. Only claim success after verifying the files.",
                ["profile.pdf"],
                ["AGENTS.md", "SELF.md", "USER.md"]),
            CancellationToken.None);

        var userContent = await File.ReadAllTextAsync(Path.Combine(_root, "agents", "main", "USER.md"));
        Assert.True(result.Success);
        Assert.Contains(result.SourceFilesFound, path => path.EndsWith("profile.pdf", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.SourceFilesRead, path => path.EndsWith("profile.pdf", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, result.VerifiedFiles.Count);
        Assert.Contains("director of platform engineering", userContent);
        Assert.DoesNotContain("concise/moderate/detailed", userContent);
        Assert.DoesNotContain("Role: needed", userContent);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private EngagementFileMaintenanceService CreateService(IAttachmentTextExtractionService? textExtractor)
        => new(
            Options.Create(new LeanKernelConfig
            {
                Wiki = new WikiConfig { BasePath = Path.Combine(_root, "wiki") },
                Agents = new AgentsConfig { BasePath = Path.Combine(_root, "agents") }
            }),
            textExtractor,
            NullLogger<EngagementFileMaintenanceService>.Instance);

    private sealed class FakeExtractor(string text) : IAttachmentTextExtractionService
    {
        public bool CanExtractText(string? contentType, string? fileName) => true;

        public Task<string?> ExtractTextAsync(string? contentType, string? fileName, byte[] bytes, CancellationToken ct)
            => Task.FromResult<string?>(text);
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Tests.Unit.Archivist;

public sealed class WikiMigrationServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _wikiPath;
    private readonly LeanKernelConfig _config;

    public WikiMigrationServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"lk-wiki-migrate-{Guid.NewGuid():N}");
        _wikiPath = Path.Combine(_tempRoot, "wiki");
        Directory.CreateDirectory(_wikiPath);
        _config = new LeanKernelConfig { Wiki = new WikiConfig { BasePath = _wikiPath } };
    }

    [Fact]
    public async Task MigrateAsync_MigratesLegacyLlmFile_AndWritesSentinel()
    {
        var llmDir = Path.Combine(_wikiPath, "llm");
        Directory.CreateDirectory(llmDir);
        var sourceFile = Path.Combine(llmDir, "llm-2026-05-10-alfero-profile.md");
        await File.WriteAllTextAsync(sourceFile, LegacyMarkdown("who", "Alfero Chingono", "Alfero prefers concise responses."));

        var store = CreateStore();
        var service = new WikiMigrationService(store, Options.Create(_config), NullLogger<WikiMigrationService>.Instance);

        var result = await service.MigrateAsync(CancellationToken.None);

        Assert.Equal(1, result.Migrated);
        Assert.Equal(0, result.Quarantined);
        Assert.True(File.Exists(result.SentinelPath));
        Assert.False(File.Exists(sourceFile));

        var migrated = await store.GetAsync("who-alfero-chingono", CancellationToken.None);
        Assert.NotNull(migrated);
        Assert.Equal("Alfero Chingono", migrated.Subject);
        Assert.Contains(migrated.Facts, f => f.Claim.Contains("concise", StringComparison.OrdinalIgnoreCase));

        var rerun = await service.MigrateAsync(CancellationToken.None);
        Assert.Equal(0, rerun.Migrated);
    }

    [Fact]
    public async Task MigrateAsync_CrossDimensionSubjectCollision_QuarantinesConflictingFile()
    {
        var llmDir = Path.Combine(_wikiPath, "llm");
        Directory.CreateDirectory(llmDir);
        var firstFile = Path.Combine(llmDir, "who-assistant.md");
        var secondFile = Path.Combine(llmDir, "what-assistant.md");
        await File.WriteAllTextAsync(firstFile, LegacyMarkdown("who", "Assistant", "Assistant is the chatbot."));
        await File.WriteAllTextAsync(secondFile, LegacyMarkdown("what", "Assistant", "Assistant is a product."));

        var store = CreateStore();
        var service = new WikiMigrationService(store, Options.Create(_config), NullLogger<WikiMigrationService>.Instance);

        var result = await service.MigrateAsync(CancellationToken.None);

        Assert.Equal(1, result.Migrated);
        Assert.Equal(1, result.Quarantined);

        var metaFolder = _config.Wiki.MetaFolder;
        var quarantinedWhat = Path.Combine(_wikiPath, metaFolder, "quarantine", "llm", "what-assistant.md");
        var quarantinedWho = Path.Combine(_wikiPath, metaFolder, "quarantine", "llm", "who-assistant.md");
        Assert.True(File.Exists(quarantinedWhat) || File.Exists(quarantinedWho));
    }

    private IWikiStore CreateStore()
        => new WikiStore(Options.Create(_config), NullLogger<WikiStore>.Instance);

    private static string LegacyMarkdown(string dimension, string subject, string claim)
        => $$"""
---
id: llm-{{Guid.NewGuid():N}}
dimension: {{dimension}}
subject: {{subject}}
lastAccessed: {{DateTimeOffset.UtcNow:O}}
accessCount: 0
---

# {{subject}}

- {{claim}} <!--{confidence: 0.90, source: conversation:test, confirmed: 2026-05-10, tokens: 8}-->
""";

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}

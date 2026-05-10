using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Host.Services;
using NSubstitute;

namespace LeanKernel.Tests.Unit.Host;

public sealed class IdentityFileUpdateServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _agentsDir;

    public IdentityFileUpdateServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_identity_{Guid.NewGuid():N}");
        _agentsDir = Path.Combine(_tempDir, "agents");
        Directory.CreateDirectory(Path.Combine(_agentsDir, "main"));
    }

    [Fact]
    public async Task UpdateFromTurnAsync_UpdatesUserAndSelfIdentitySections()
    {
        var userPath = Path.Combine(_agentsDir, "main", "USER.md");
        var selfPath = Path.Combine(_agentsDir, "main", "SELF.md");
        await File.WriteAllTextAsync(userPath, """
            # User

            ## Role

            unknown

            ## Organization

            unknown

            ## Expertise

            """);
        await File.WriteAllTextAsync(selfPath, """
            # Agent

            ## Personalization

            unknown

            ## Limitations Observed

            ## Capability Gaps

            """);

        var service = CreateService();
        await service.UpdateFromTurnAsync(
            "I'm a senior engineer at Contoso and specialized in developer tooling.",
            "Based on your request, I don't have access to payroll records.",
            "session-1",
            CancellationToken.None);

        var userContent = await File.ReadAllTextAsync(userPath);
        var selfContent = await File.ReadAllTextAsync(selfPath);

        Assert.Contains("## Role", userContent);
        Assert.Contains("senior engineer", userContent);
        Assert.Contains("## Organization", userContent);
        Assert.Contains("Contoso", userContent);
        Assert.Contains("## Expertise", userContent);
        Assert.Contains("specialized in developer tooling", userContent);
        Assert.Contains("## Personalization", selfContent);
        Assert.Contains("high", selfContent);
        Assert.Contains("## Limitations Observed", selfContent);
        Assert.Contains("Limitation detected in response", selfContent);
        Assert.Contains("## Capability Gaps", selfContent);
        Assert.Contains("payroll records", selfContent);
    }

    [Fact]
    public async Task UpdateFromTurnAsync_MissingIdentityFiles_DoesNotThrow()
    {
        var service = CreateService();

        var exception = await Record.ExceptionAsync(() => service.UpdateFromTurnAsync(
            "I'm a developer at Contoso.",
            "I don't have access to that system.",
            "session-2",
            CancellationToken.None));

        Assert.Null(exception);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private IdentityFileUpdateService CreateService() => new(
        Options.Create(new LeanKernelConfig
        {
            Agents = new AgentsConfig { BasePath = _agentsDir }
        }),
        Substitute.For<IWikiStore>(),
        NullLogger<IdentityFileUpdateService>.Instance);
}

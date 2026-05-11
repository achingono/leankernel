using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Thinker.Enhancement;
using NSubstitute;

namespace LeanKernel.Tests.Unit.Thinker.Enhancement;

public sealed class EngagementFileMaintenanceResponseEnhancerTests
{
    [Fact]
    public async Task EnhanceResponseAsync_EngagementUpdateRequest_ReplacesUnsupportedModelClaim()
    {
        var service = Substitute.For<IEngagementFileMaintenanceService>();
        service.MaintainAsync(Arg.Any<EngagementFileMaintenanceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new EngagementFileMaintenanceResult
            {
                Success = true,
                SourceFilesFound = ["/data/documents/profile.pdf"],
                SourceFilesRead = ["/data/documents/profile.pdf"],
                ChangedFiles = ["/data/agents/main/USER.md"],
                VerifiedFiles = ["/data/agents/main/USER.md"],
                SourceExcerpts = ["- profile.pdf: Director of platform engineering"]
            });
        var enhancer = new EngagementFileMaintenanceResponseEnhancer(
            service,
            NullLogger<EngagementFileMaintenanceResponseEnhancer>.Instance);

        var result = await enhancer.EnhanceResponseAsync(
            "Read `profile.pdf` and update engagement files.",
            "Engagement files updated successfully.",
            new ConversationContext
            {
                SystemPrompt = "test",
                History = [],
                WikiLeanKernels = [],
                RetrievedLeanKernels = [],
                ActiveToolNames = []
            },
            CancellationToken.None);

        Assert.Contains("Engagement file maintenance completed with verified state.", result);
        Assert.Contains("Source files read", result);
        Assert.Contains("/data/agents/main/USER.md", result);
        Assert.DoesNotContain("Engagement files updated successfully.", result);
        await service.Received(1).MaintainAsync(
            Arg.Is<EngagementFileMaintenanceRequest>(request => request.SourceDocumentNames.Contains("profile.pdf")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnhanceResponseAsync_EngagementUpdateRequest_ExtractsOnlySourceDocuments()
    {
        var service = Substitute.For<IEngagementFileMaintenanceService>();
        service.MaintainAsync(Arg.Any<EngagementFileMaintenanceRequest>(), Arg.Any<CancellationToken>())
            .Returns(new EngagementFileMaintenanceResult
            {
                Success = false,
                Errors = ["test"]
            });
        var enhancer = new EngagementFileMaintenanceResponseEnhancer(
            service,
            NullLogger<EngagementFileMaintenanceResponseEnhancer>.Instance);

        await enhancer.EnhanceResponseAsync(
            "Read e2e-profile.md and use the insights to update the engagement files AGENTS.md, SELF.md, and USER.md.",
            "done",
            new ConversationContext
            {
                SystemPrompt = "test",
                History = [],
                WikiLeanKernels = [],
                RetrievedLeanKernels = [],
                ActiveToolNames = []
            },
            CancellationToken.None);

        await service.Received(1).MaintainAsync(
            Arg.Is<EngagementFileMaintenanceRequest>(request =>
                request.SourceDocumentNames.SequenceEqual(new[] { "e2e-profile.md" })),
            Arg.Any<CancellationToken>());
    }
}

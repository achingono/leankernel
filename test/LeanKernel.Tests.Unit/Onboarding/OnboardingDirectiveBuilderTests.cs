using FluentAssertions;

using LeanKernel.Logic.Memory;
using LeanKernel.Services.Learning.Onboarding;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Onboarding;

public sealed class OnboardingDirectiveBuilderTests
{
    [Fact]
    public async Task BuildAndPersistDirectivesAsync_PersistsDirectiveForEachGap()
    {
        var memoryService = new Mock<IMemoryService>();
        var sut = new OnboardingDirectiveBuilder(memoryService.Object, NullLogger<OnboardingDirectiveBuilder>.Instance);
        var gaps = new[]
        {
            new OnboardingGap("profile_missing", "Please add your profile details.", 1),
            new OnboardingGap("memory_intent_missing", "Tell me your goals.", 2),
        };

        await sut.BuildAndPersistDirectivesAsync(Guid.NewGuid(), Guid.NewGuid(), gaps);

        memoryService.Verify(x => x.PutPageAsync("onboarding/directive/profile_missing", "Please add your profile details.", It.IsAny<CancellationToken>()), Times.Once);
        memoryService.Verify(x => x.PutPageAsync("onboarding/directive/memory_intent_missing", "Tell me your goals.", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuildAndPersistDirectivesAsync_NoGaps_DoesNothing()
    {
        var memoryService = new Mock<IMemoryService>();
        var sut = new OnboardingDirectiveBuilder(memoryService.Object, NullLogger<OnboardingDirectiveBuilder>.Instance);

        await sut.BuildAndPersistDirectivesAsync(Guid.NewGuid(), Guid.NewGuid(), []);

        memoryService.Verify(x => x.PutPageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

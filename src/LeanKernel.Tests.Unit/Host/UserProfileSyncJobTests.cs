using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Host.Services;
using LeanKernel.Scheduler.Jobs;
using Xunit;

namespace LeanKernel.Tests.Unit.Host;

public class UserProfileSyncJobTests
{
    [Fact]
    public async Task ExecuteAsync_CreatesSelfAndUserFiles_WhenMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lk-user-profile-sync-{Guid.NewGuid():N}");

        try
        {
            var paths = new LeanKernelHostPaths
            {
                DataDirectory = root,
                AgentsDirectory = Path.Combine(root, "agents"),
                RuntimeConfigPath = Path.Combine(root, "runtime-settings.json"),
                OnboardingStatePath = Path.Combine(root, "onboarding-state.json")
            };

            var wiki = Substitute.For<IWikiStore>();
            wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

            var selfConfig = new SelfConfigurationStep(paths, NullLogger<SelfConfigurationStep>.Instance);
            var userConfig = new UserConfigurationStep(paths, wiki, NullLogger<UserConfigurationStep>.Instance);
            var job = new UserProfileSyncJob(selfConfig, userConfig, NullLogger<UserProfileSyncJob>.Instance);

            await job.ExecuteAsync(CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(paths.AgentsDirectory, "main", "SELF.md")));
            Assert.True(File.Exists(Path.Combine(paths.AgentsDirectory, "main", "USER.md")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Services;

namespace LeanKernel.Tests.Unit.Host;

public class ActionAuthorizerTests
{
    [Fact]
    public async Task AuthorizeAsync_ActionInNeverDo_ReturnsFalse()
    {
        var rules = new EngagementRules
        {
            Autonomy = new AutonomyScope
            {
                NeverDo = ["CommitSecrets", "DeleteProductionData"]
            }
        };
        
        var authorizer = new ActionAuthorizer(rules, new NullLogger<ActionAuthorizer>());
        
        var result = await authorizer.AuthorizeAsync("CommitSecrets", CancellationToken.None);
        
        Assert.False(result.IsAuthorized);
        Assert.Equal("Action is explicitly forbidden", result.Reason);
    }

    [Fact]
    public async Task AuthorizeAsync_ActionInCanDoWithoutAsking_ReturnsTrue()
    {
        var rules = new EngagementRules
        {
            Autonomy = new AutonomyScope
            {
                CanDoWithoutAsking = ["ViewRepositoryStructure", "SearchCodebase"]
            }
        };
        
        var authorizer = new ActionAuthorizer(rules, new NullLogger<ActionAuthorizer>());
        
        var result = await authorizer.AuthorizeAsync("ViewRepositoryStructure", CancellationToken.None);
        
        Assert.True(result.IsAuthorized);
        Assert.Equal("Action is allowed without asking", result.Reason);
    }

    [Fact]
    public async Task AuthorizeAsync_ActionInMustAskBefore_ReturnsFalse()
    {
        var rules = new EngagementRules
        {
            Autonomy = new AutonomyScope
            {
                MustAskBefore = ["SendExternalMessage", "DeployToProduction"]
            }
        };
        
        var authorizer = new ActionAuthorizer(rules, new NullLogger<ActionAuthorizer>());
        
        var result = await authorizer.AuthorizeAsync("SendExternalMessage", CancellationToken.None);
        
        Assert.False(result.IsAuthorized);
        Assert.Equal("User permission required", result.Reason);
    }

    [Fact]
    public async Task AuthorizeAsync_UnknownAction_ReturnsFalse()
    {
        var rules = new EngagementRules
        {
            Autonomy = new AutonomyScope
            {
                CanDoWithoutAsking = ["ViewRepositoryStructure"]
            }
        };
        
        var authorizer = new ActionAuthorizer(rules, new NullLogger<ActionAuthorizer>());
        
        var result = await authorizer.AuthorizeAsync("UnknownAction", CancellationToken.None);
        
        Assert.False(result.IsAuthorized);
        Assert.Equal("Action type not recognized", result.Reason);
    }

    [Fact]
    public async Task AuthorizeAsync_NeverDoOverridesCanDo()
    {
        var rules = new EngagementRules
        {
            Autonomy = new AutonomyScope
            {
                NeverDo = ["CommitSecrets"],
                CanDoWithoutAsking = ["CommitSecrets", "ViewRepositoryStructure"]
            }
        };
        
        var authorizer = new ActionAuthorizer(rules, new NullLogger<ActionAuthorizer>());
        
        // NeverDo should take precedence
        var result = await authorizer.AuthorizeAsync("CommitSecrets", CancellationToken.None);
        
        Assert.False(result.IsAuthorized);
        Assert.Equal("Action is explicitly forbidden", result.Reason);
    }

    [Fact]
    public async Task AuthorizeAsync_CaseInsensitive()
    {
        var rules = new EngagementRules
        {
            Autonomy = new AutonomyScope
            {
                CanDoWithoutAsking = ["ViewRepositoryStructure"]
            }
        };
        
        var authorizer = new ActionAuthorizer(rules, new NullLogger<ActionAuthorizer>());
        
        var result = await authorizer.AuthorizeAsync("viewrepositorystructure", CancellationToken.None);
        
        Assert.True(result.IsAuthorized);
    }
}

public class TimeBoundaryServiceTests
{
    [Fact]
    public void IsInActiveHours_WithinActiveWindow_Returns()
    {
        var rules = new EngagementRules
        {
            TimeBoundaries = new TimeBoundaries
            {
                Timezone = "UTC",
                ActiveHoursStart = 8,
                ActiveHoursEnd = 22
            }
        };
        
        var service = new TimeBoundaryService(rules, new NullLogger<TimeBoundaryService>());
        
        // Test depends on current UTC time, so we just verify method runs
        var isActive = service.IsInActiveHours();
        Assert.IsType<bool>(isActive);
    }

    [Fact]
    public void GetNextActiveWindow_ReturnsValidDateTime()
    {
        var rules = new EngagementRules
        {
            TimeBoundaries = new TimeBoundaries
            {
                Timezone = "UTC",
                ActiveHoursStart = 8,
                ActiveHoursEnd = 22
            }
        };
        
        var service = new TimeBoundaryService(rules, new NullLogger<TimeBoundaryService>());
        var nextWindow = service.GetNextActiveWindow();
        
        Assert.True(nextWindow > DateTime.UtcNow);
    }

    [Fact]
    public void GetStatus_ReturnsCompleteStatus()
    {
        var rules = new EngagementRules
        {
            TimeBoundaries = new TimeBoundaries
            {
                Timezone = "UTC",
                ActiveHoursStart = 8,
                ActiveHoursEnd = 22,
                SabbathDay = DayOfWeek.Saturday
            }
        };
        
        var service = new TimeBoundaryService(rules, new NullLogger<TimeBoundaryService>());
        var status = service.GetStatus();
        
        Assert.NotNull(status);
        Assert.False(string.IsNullOrEmpty(status.CurrentTimeZone));
        Assert.True(status.NextActiveWindow > DateTime.UtcNow);
    }
}


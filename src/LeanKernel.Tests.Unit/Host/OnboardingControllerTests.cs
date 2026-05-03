using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Controllers;
using LeanKernel.Host.Services;

namespace LeanKernel.Tests.Unit.Host;

public class OnboardingControllerTests
{
    private static IOnboardingStateStore CreateIncompleteStore()
    {
        var store = Substitute.For<IOnboardingStateStore>();
        store.IsCompletedAsync(Arg.Any<CancellationToken>()).Returns(false);
        return store;
    }

    private static AgentsConfigurationStep CreateStubAgentsStep()
    {
        var paths = new LeanKernelHostPaths
        {
            DataDirectory = Path.GetTempPath(),
            RuntimeConfigPath = Path.Combine(Path.GetTempPath(), "runtime.json"),
            OnboardingStatePath = Path.Combine(Path.GetTempPath(), "onboarding.json")
        };
        var rulesProvider = Substitute.For<IEngagementRulesProvider>();
        rulesProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(new EngagementRules());
        var logger = Substitute.For<ILogger<AgentsConfigurationStep>>();
        return new AgentsConfigurationStep(paths, rulesProvider, logger);
    }

    [Fact]
    public async Task GetStatus_ReturnsOk()
    {
        var orchestrator = Substitute.For<IOnboardingOrchestrator>();
        orchestrator.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new OnboardingStatus { Completed = false, UpdatedAt = DateTimeOffset.UtcNow });

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore(), CreateStubAgentsStep());
        var result = await controller.GetStatus(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task SaveDraft_ReturnsOk()
    {
        var orchestrator = Substitute.For<IOnboardingOrchestrator>();
        orchestrator.SaveDraftAsync(Arg.Any<OnboardingConfigInput>(), Arg.Any<CancellationToken>())
            .Returns(new OnboardingStatus { Completed = false, UpdatedAt = DateTimeOffset.UtcNow });

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore(), CreateStubAgentsStep());
        var result = await controller.SaveDraft(new OnboardingConfigInput(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetDraft_ReturnsOk()
    {
        var orchestrator = Substitute.For<IOnboardingOrchestrator>();
        orchestrator.GetDraftAsync(Arg.Any<CancellationToken>())
            .Returns(new OnboardingConfigInput());

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore(), CreateStubAgentsStep());
        var result = await controller.GetDraft(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Validate_ReturnsOk()
    {
        var orchestrator = Substitute.For<IOnboardingOrchestrator>();
        orchestrator.ValidateAsync(Arg.Any<CancellationToken>())
            .Returns(new OnboardingValidationResult());

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore(), CreateStubAgentsStep());
        var result = await controller.Validate(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Complete_FailedValidation_ReturnsBadRequest()
    {
        var orchestrator = Substitute.For<IOnboardingOrchestrator>();
        orchestrator.CompleteAsync(Arg.Any<CancellationToken>())
            .Returns(new OnboardingCompletionResult
            {
                Success = false,
                Message = "failed",
                Status = new OnboardingStatus { UpdatedAt = DateTimeOffset.UtcNow },
                Validation = new OnboardingValidationResult()
            });

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore(), CreateStubAgentsStep());
        var result = await controller.Complete(CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Complete_Success_ReturnsOk()
    {
        var orchestrator = Substitute.For<IOnboardingOrchestrator>();
        orchestrator.CompleteAsync(Arg.Any<CancellationToken>())
            .Returns(new OnboardingCompletionResult
            {
                Success = true,
                Message = "ok",
                Status = new OnboardingStatus
                {
                    Completed = true,
                    CompletedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                Validation = new OnboardingValidationResult
                {
                    Steps =
                    [
                        new OnboardingStepResult
                        {
                            Step = "filesystem",
                            Success = true,
                            Message = "ok"
                        }
                    ]
                }
            });

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore(), CreateStubAgentsStep());
        var result = await controller.Complete(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public void GetAgentPresets_ReturnsOk()
    {
        var orchestrator = Substitute.For<IOnboardingOrchestrator>();
        var agentsStep = CreateStubAgentsStep();

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore(), agentsStep);
        var result = controller.GetAgentPresets();

        Assert.IsType<OkObjectResult>(result);
        var okResult = (OkObjectResult)result;
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task InitializeAgents_WithValidPreset_ReturnsOk()
    {
        var orchestrator = Substitute.For<IOnboardingOrchestrator>();
        var agentsStep = CreateStubAgentsStep();

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore(), agentsStep);
        var request = new AgentsInitializeRequest { PresetName = "basic" };
        var result = await controller.InitializeAgents(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task InitializeAgents_WithInvalidPreset_DefaultsToBasic()
    {
        var orchestrator = Substitute.For<IOnboardingOrchestrator>();
        var agentsStep = CreateStubAgentsStep();

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore(), agentsStep);
        var request = new AgentsInitializeRequest { PresetName = "invalid-preset-xyz" };
        var result = await controller.InitializeAgents(request, CancellationToken.None);

        // Invalid presets default to basic, so should return Ok
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ValidateAgents_ReturnsOk()
    {
        var orchestrator = Substitute.For<IOnboardingOrchestrator>();
        var agentsStep = CreateStubAgentsStep();

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore(), agentsStep);
        var result = await controller.ValidateAgents(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task UpdateAgentSection_WithValidSection_ReturnsOk()
    {
        var orchestrator = Substitute.For<IOnboardingOrchestrator>();
        var agentsStep = CreateStubAgentsStep();

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore(), agentsStep);
        var request = new AgentsSectionUpdateRequest 
        { 
            SectionName = "Agent Personality", 
            Content = "Test content" 
        };
        var result = await controller.UpdateAgentSection("Agent Personality", request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }
}

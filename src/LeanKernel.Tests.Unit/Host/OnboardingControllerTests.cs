using Microsoft.AspNetCore.Mvc;
using NSubstitute;
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

    [Fact]
    public async Task GetStatus_ReturnsOk()
    {
        var orchestrator = Substitute.For<IOnboardingOrchestrator>();
        orchestrator.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(new OnboardingStatus { Completed = false, UpdatedAt = DateTimeOffset.UtcNow });

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore());
        var result = await controller.GetStatus(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task SaveDraft_ReturnsOk()
    {
        var orchestrator = Substitute.For<IOnboardingOrchestrator>();
        orchestrator.SaveDraftAsync(Arg.Any<OnboardingConfigInput>(), Arg.Any<CancellationToken>())
            .Returns(new OnboardingStatus { Completed = false, UpdatedAt = DateTimeOffset.UtcNow });

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore());
        var result = await controller.SaveDraft(new OnboardingConfigInput(), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetDraft_ReturnsOk()
    {
        var orchestrator = Substitute.For<IOnboardingOrchestrator>();
        orchestrator.GetDraftAsync(Arg.Any<CancellationToken>())
            .Returns(new OnboardingConfigInput());

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore());
        var result = await controller.GetDraft(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Validate_ReturnsOk()
    {
        var orchestrator = Substitute.For<IOnboardingOrchestrator>();
        orchestrator.ValidateAsync(Arg.Any<CancellationToken>())
            .Returns(new OnboardingValidationResult());

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore());
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

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore());
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

        var controller = new OnboardingController(orchestrator, CreateIncompleteStore());
        var result = await controller.Complete(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }
}

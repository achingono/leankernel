using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LeanKernel.Host.Services;
using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Host.Controllers;

[ApiController]
[Route("api/onboarding")]
[Authorize(Policy = AuthConstants.PolicyAdminOnly)]
public sealed class OnboardingController : ControllerBase
{
    private readonly IOnboardingOrchestrator _orchestrator;

    public OnboardingController(IOnboardingOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        return Ok(await _orchestrator.GetStatusAsync(ct));
    }

    [HttpGet("draft")]
    public async Task<IActionResult> GetDraft(CancellationToken ct)
    {
        return Ok(await _orchestrator.GetDraftAsync(ct));
    }

    [HttpPut("draft")]
    public async Task<IActionResult> SaveDraft(
        [FromBody] OnboardingConfigInput draft,
        CancellationToken ct)
    {
        var status = await _orchestrator.SaveDraftAsync(draft, ct);
        return Ok(status);
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate(CancellationToken ct)
    {
        var validation = await _orchestrator.ValidateAsync(ct);
        return Ok(validation);
    }

    [HttpPost("complete")]
    public async Task<IActionResult> Complete(CancellationToken ct)
    {
        var result = await _orchestrator.CompleteAsync(ct);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }
}

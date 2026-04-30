using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LeanKernel.Host.Services;
using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Host.Controllers;

[ApiController]
[Route("api/onboarding")]
public sealed class OnboardingController : ControllerBase
{
    private readonly IOnboardingOrchestrator _orchestrator;
    private readonly IOnboardingStateStore _stateStore;

    public OnboardingController(IOnboardingOrchestrator orchestrator, IOnboardingStateStore stateStore)
    {
        _orchestrator = orchestrator;
        _stateStore = stateStore;
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
        if (!await AllowAccessAsync(ct))
            return Forbid();
        return Ok(await _orchestrator.GetDraftAsync(ct));
    }

    [HttpPut("draft")]
    public async Task<IActionResult> SaveDraft(
        [FromBody] OnboardingConfigInput draft,
        CancellationToken ct)
    {
        if (!await AllowAccessAsync(ct))
            return Forbid();
        var status = await _orchestrator.SaveDraftAsync(draft, ct);
        return Ok(status);
    }

    [HttpPost("validate")]
    public async Task<IActionResult> Validate(CancellationToken ct)
    {
        if (!await AllowAccessAsync(ct))
            return Forbid();
        var validation = await _orchestrator.ValidateAsync(ct);
        return Ok(validation);
    }

    [HttpPost("complete")]
    public async Task<IActionResult> Complete(CancellationToken ct)
    {
        if (!await AllowAccessAsync(ct))
            return Forbid();
        var result = await _orchestrator.CompleteAsync(ct);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>
    /// During first-run (onboarding not complete): allow anonymous access.
    /// After onboarding: require authenticated admin.
    /// </summary>
    private async Task<bool> AllowAccessAsync(CancellationToken ct)
    {
        var completed = await _stateStore.IsCompletedAsync(ct);
        if (!completed)
            return true; // First-run: anonymous OK

        return User.Identity?.IsAuthenticated == true &&
               User.IsInRole(AuthConstants.RoleAdmin);
    }
}

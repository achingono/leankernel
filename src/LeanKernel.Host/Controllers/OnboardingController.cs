using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LeanKernel.Host.Services;
using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Host.Controllers;

/// <summary>
/// Represents the onboarding controller.
/// </summary>
[ApiController]
[Route("api/onboarding")]
public sealed class OnboardingController : ControllerBase
{
    private readonly IOnboardingOrchestrator _orchestrator;
    private readonly IOnboardingStateStore _stateStore;
    private readonly AgentsConfigurationStep _agentsStep;

    /// <summary>
    /// Represents the onboarding controller.
    /// </summary>
    public OnboardingController(
        IOnboardingOrchestrator orchestrator,
        IOnboardingStateStore stateStore,
        AgentsConfigurationStep agentsStep)
    {
        _orchestrator = orchestrator;
        _stateStore = stateStore;
        _agentsStep = agentsStep;
    }

    /// <summary>
    /// Executes the get status operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        return Ok(await _orchestrator.GetStatusAsync(ct));
    }

    /// <summary>
    /// Executes the get draft operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    [HttpGet("draft")]
    public async Task<IActionResult> GetDraft(CancellationToken ct)
    {
        if (!await AllowAccessAsync(ct))
            return Forbid();
        return Ok(await _orchestrator.GetDraftAsync(ct));
    }

    /// <summary>
    /// Represents the save draft.
    /// </summary>
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

    /// <summary>
    /// Executes the validate operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    [HttpPost("validate")]
    public async Task<IActionResult> Validate(CancellationToken ct)
    {
        if (!await AllowAccessAsync(ct))
            return Forbid();
        var validation = await _orchestrator.ValidateAsync(ct);
        return Ok(validation);
    }

    /// <summary>
    /// Executes the complete operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
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
    /// Executes the get agent presets operation.
    /// </summary>
    /// <returns>The operation result.</returns>
    [HttpGet("agents/presets")]
    [AllowAnonymous]
    public IActionResult GetAgentPresets()
    {
        var presets = _agentsStep.GetAvailablePresets();
        return Ok(presets);
    }

    /// <summary>
    /// Represents the initialize agents.
    /// </summary>
    [HttpPost("agents/initialize")]
    public async Task<IActionResult> InitializeAgents(
        [FromBody] AgentsInitializeRequest request,
        CancellationToken ct)
    {
        if (!await AllowAccessAsync(ct))
            return Forbid();

        try
        {
            var result = await _agentsStep.InitializeAsync(request.PresetName);
            if (!result.Success)
                return BadRequest(new AgentsInitializeResponse
                {
                    Success = false,
                    Message = result.Message
                });

            return Ok(new AgentsInitializeResponse
            {
                Success = true,
                Message = result.Message,
                Rules = result.Rules
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new AgentsInitializeResponse
            {
                Success = false,
                Message = $"Invalid preset: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Executes the validate agents operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    [HttpGet("agents/validate")]
    public async Task<IActionResult> ValidateAgents(CancellationToken ct)
    {
        if (!await AllowAccessAsync(ct))
            return Forbid();

        var result = await _agentsStep.ValidateAsync();
        return Ok(new AgentsValidateResponse
        {
            Success = result.Success,
            IsValid = result.IsValid ?? false,
            Errors = result.Errors,
            Warnings = result.Warnings
        });
    }

    /// <summary>
    /// Represents the update agent section.
    /// </summary>
    [HttpPost("agents/sections/{sectionName}")]
    public async Task<IActionResult> UpdateAgentSection(
        string sectionName,
        [FromBody] AgentsSectionUpdateRequest request,
        CancellationToken ct)
    {
        if (!await AllowAccessAsync(ct))
            return Forbid();

        try
        {
            var result = await _agentsStep.UpdateSectionAsync(sectionName, request.Content);
            if (!result.Success)
                return BadRequest(new { success = false, message = result.Message });

            return Ok(new { success = true, message = result.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = $"Failed to update section: {ex.Message}" });
        }
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

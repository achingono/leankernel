using Microsoft.AspNetCore.Mvc;
using LeanKernel.Host.Models.Routing;
using LeanKernel.Host.Services;

namespace LeanKernel.Host.Controllers;

/// <summary>
/// Represents the routing config controller.
/// </summary>
[ApiController]
[Route("api/routing-config")]
public sealed class RoutingConfigController : ControllerBase
{
    private readonly ILiteLlmRoutingConfigService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutingConfigController" /> class.
    /// </summary>
    /// <param name="service">The service.</param>
    public RoutingConfigController(ILiteLlmRoutingConfigService service)
    {
        _service = service;
    }

    /// <summary>
    /// Executes the get config operation.
    /// </summary>
    /// <returns>The operation result.</returns>
    [HttpGet]
    public IActionResult GetConfig()
    {
        var config = _service.Load();
        var errors = _service.Validate(config);
        var keyStatuses = _service.GetKeyStatuses(config);

        return Ok(new RoutingConfigResponse
        {
            Config = config,
            ValidationErrors = errors,
            KeyStatuses = keyStatuses
        });
    }

    /// <summary>
    /// Represents the save config.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> SaveConfig(
        [FromBody] RoutingConfigSaveRequest request,
        CancellationToken ct)
    {
        var errors = _service.Validate(request.Config);
        var hardErrors = errors.Where(e => e.Severity == "error").ToList();

        var currentYaml = _service.GenerateYaml(_service.Load());
        var newYaml = _service.GenerateYaml(request.Config);
        var diff = _service.ComputeDiff(currentYaml, newYaml);

        if (hardErrors.Count > 0)
        {
            return UnprocessableEntity(new RoutingConfigSaveResponse
            {
                Saved = false,
                YamlDiff = diff,
                ValidationErrors = errors
            });
        }

        if (!request.DryRun)
            await _service.SaveAsync(request.Config, ct);

        return Ok(new RoutingConfigSaveResponse
        {
            Saved = !request.DryRun,
            YamlDiff = diff,
            ValidationErrors = errors
        });
    }

    /// <summary>
    /// Executes the get raw yaml operation.
    /// </summary>
    /// <returns>The operation result.</returns>
    [HttpGet("raw")]
    public IActionResult GetRawYaml()
    {
        var yaml = _service.GetRawYaml();
        return Ok(new { yaml });
    }

    /// <summary>
    /// Represents the save raw yaml.
    /// </summary>
    [HttpPut("raw")]
    public async Task<IActionResult> SaveRawYaml(
        [FromBody] RawYamlSaveRequest request,
        CancellationToken ct)
    {
        try
        {
            await _service.SaveRawYamlAsync(request.Yaml, ct);
            return Ok(new { saved = true });
        }
        catch (Exception ex)
        {
            return UnprocessableEntity(new { saved = false, error = ex.Message });
        }
    }
}

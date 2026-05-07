using Microsoft.AspNetCore.Mvc;
using LeanKernel.Host.Models.Routing;
using LeanKernel.Host.Services;

namespace LeanKernel.Host.Controllers;

[ApiController]
[Route("api/routing-config")]
public sealed class RoutingConfigController : ControllerBase
{
    private readonly ILiteLlmRoutingConfigService _service;

    public RoutingConfigController(ILiteLlmRoutingConfigService service)
    {
        _service = service;
    }

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
}

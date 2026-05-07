using LeanKernel.Host.Models.Routing;
using LeanKernel.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace LeanKernel.Host.Controllers;

[ApiController]
[Route("api/model-limit-drift")]
public sealed class ModelLimitDriftController(IModelLimitDriftService driftService) : ControllerBase
{
    [HttpGet]
    public async Task<DriftReport> Preview(CancellationToken ct) =>
        await driftService.PreviewDriftAsync(ct);
}

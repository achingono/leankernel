using LeanKernel.Host.Models.Routing;
using LeanKernel.Host.Services;
using Microsoft.AspNetCore.Mvc;

namespace LeanKernel.Host.Controllers;

/// <summary>
/// Represents the model limit drift controller.
/// </summary>
/// <summary>
/// Represents the model limit drift controller.
/// </summary>
[ApiController]
[Route("api/model-limit-drift")]
public sealed class ModelLimitDriftController(IModelLimitDriftService driftService) : ControllerBase
{
    /// <summary>
    /// Executes the preview operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    [HttpGet]
    public async Task<DriftReport> Preview(CancellationToken ct) =>
        await driftService.PreviewDriftAsync(ct);
}

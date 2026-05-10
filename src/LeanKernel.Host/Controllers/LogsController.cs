using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LeanKernel.Host.Services;
using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Host.Controllers;

/// <summary>
/// Represents the logs controller.
/// </summary>
[ApiController]
[Route("api/logs")]
[Authorize(Policy = AuthConstants.PolicyAdminOnly)]
public sealed class LogsController : ControllerBase
{
    private readonly LogReaderService _logReader;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogsController" /> class.
    /// </summary>
    /// <param name="logReader">The log reader.</param>
    public LogsController(LogReaderService logReader)
    {
        _logReader = logReader;
    }

    /// <summary>
    /// Represents the search logs.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SearchLogs(
        [FromQuery] string? q = null,
        [FromQuery] string? level = null,
        [FromQuery] string? since = null,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        var result = await _logReader.SearchAsync(q, level, since, limit, ct);
        return Ok(result);
    }

    /// <summary>
    /// Executes the list files operation.
    /// </summary>
    /// <returns>The operation result.</returns>
    [HttpGet("files")]
    public IActionResult ListFiles()
    {
        return Ok(_logReader.ListLogFiles());
    }
}

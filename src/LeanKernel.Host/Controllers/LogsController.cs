using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LeanKernel.Host.Services;
using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Host.Controllers;

[ApiController]
[Route("api/logs")]
[Authorize(Policy = AuthConstants.PolicyAdminOnly)]
public sealed class LogsController : ControllerBase
{
    private readonly LogReaderService _logReader;

    public LogsController(LogReaderService logReader)
    {
        _logReader = logReader;
    }

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

    [HttpGet("files")]
    public IActionResult ListFiles()
    {
        return Ok(_logReader.ListLogFiles());
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LeanKernel.Host.Services;
using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Host.Controllers;

[ApiController]
[Route("api/files")]
[Authorize(Policy = AuthConstants.PolicyAdminOnly)]
public sealed class FilesController : ControllerBase
{
    private readonly FileBrowserService _browser;

    public FilesController(FileBrowserService browser)
    {
        _browser = browser;
    }

    [HttpGet("browse")]
    public IActionResult Browse([FromQuery] string? path = null)
    {
        var result = _browser.Browse(path);
        if (result.Error is not null)
            return BadRequest(new { error = result.Error });
        return Ok(result);
    }

    [HttpGet("read")]
    public async Task<IActionResult> ReadFile(
        [FromQuery] string path,
        CancellationToken ct)
    {
        var result = await _browser.ReadAsync(path, ct);
        if (result.Error is not null)
            return BadRequest(new { error = result.Error });
        return Ok(result);
    }
}

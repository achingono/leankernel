using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using LeanKernel.Host.Services;
using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Host.Controllers;

/// <summary>
/// Represents the files controller.
/// </summary>
[ApiController]
[Route("api/files")]
[Authorize(Policy = AuthConstants.PolicyAdminOnly)]
public sealed class FilesController : ControllerBase
{
    private readonly FileBrowserService _browser;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilesController" /> class.
    /// </summary>
    /// <param name="browser">The browser.</param>
    public FilesController(FileBrowserService browser)
    {
        _browser = browser;
    }

    /// <summary>
    /// Executes the browse operation.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The operation result.</returns>
    [HttpGet("browse")]
    public IActionResult Browse([FromQuery] string? path = null)
    {
        var result = _browser.Browse(path);
        if (result.Error is not null)
            return BadRequest(new { error = result.Error });
        return Ok(result);
    }

    /// <summary>
    /// Represents the read file.
    /// </summary>
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

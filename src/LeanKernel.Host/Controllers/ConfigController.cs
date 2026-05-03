using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Services.Auth;

namespace LeanKernel.Host.Controllers;

[ApiController]
[Route("api/config")]
[Authorize(Policy = AuthConstants.PolicyAdminOnly)]
public sealed class ConfigController : ControllerBase
{
    private readonly IOptions<LeanKernelConfig> _config;

    public ConfigController(IOptions<LeanKernelConfig> config)
    {
        _config = config;
    }

    [HttpGet]
    public IActionResult GetConfig()
    {
        var cfg = _config.Value;
        return Ok(new
        {
            liteLlm = new
            {
                baseUrl = cfg.LiteLlm.BaseUrl,
                apiKey = MaskSecret(cfg.LiteLlm.ApiKey),
                defaultModel = cfg.LiteLlm.DefaultModel,
                embeddingModel = cfg.LiteLlm.EmbeddingModel,
                contextWindowTokens = cfg.LiteLlm.ContextWindowTokens
            },
            qdrant = cfg.Qdrant,
            signal = new
            {
                cliPath = cfg.Signal.CliPath,
                account = MaskSecret(cfg.Signal.Account),
                enabled = cfg.Signal.Enabled,
                allowedSenders = cfg.Signal.AllowedSenders ?? []
            },
            wiki = cfg.Wiki,
            context = cfg.Context,
            scheduler = cfg.Scheduler
        });
    }

    private static string MaskSecret(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 8)
            return "***";
        return value[..4] + new string('*', value.Length - 4);
    }
}

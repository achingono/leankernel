using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Plugins.BuiltIn.Skills;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;

namespace LeanKernel.Plugins;

/// <summary>
/// Extension methods for configuring skills in the dependency injection container.
/// </summary>
public static class SkillExtensions
{
    /// <summary>
    /// Adds the LeanKernel skills and their associated hosted services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to add skills to.</param>
    /// <returns>The service collection after adding the skills.</returns>
    public static IServiceCollection AddLeanKernelSkills(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<SkillParser>();
        services.AddHttpClient("SkillHttp")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
            });
        services.TryAddSingleton<RuntimeSkillRegistry>(sp =>
        {
            var config = sp.GetRequiredService<IOptions<LeanKernel.Abstractions.Configuration.LeanKernelConfig>>().Value;
            var basePaths = config.Skills?.BasePaths ?? new List<string> { "/app/data/skills" };
            var parser = sp.GetRequiredService<SkillParser>();
            var logger = sp.GetRequiredService<ILogger<RuntimeSkillRegistry>>();
            return new RuntimeSkillRegistry(basePaths, parser, logger);
        });
        services.AddHostedService<SkillHostedService>();

        return services;
    }
}

/// <summary>
/// A hosted service that initializes and manages the runtime skill registry.
/// </summary>
public sealed class SkillHostedService : IHostedService
{
    private readonly RuntimeSkillRegistry _registry;
    private readonly IToolRegistry _toolRegistry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SkillHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref:SkillHostedService/> class.
    /// </summary>
    /// <param name="registry">The runtime skill registry.</param>
    /// <param name="toolRegistry">The tool registry.</param>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public SkillHostedService(
        RuntimeSkillRegistry registry,
        IToolRegistry toolRegistry,
        IHttpClientFactory httpClientFactory,
        ILogger<SkillHostedService> logger)
    {
        _registry = registry;
        _toolRegistry = toolRegistry;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Starts the hosted service, loading all skills and registering their tools.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registry.LoadAll();

        var dynamicTools = new List<ToolDefinition>();
        foreach (var skill in _registry.Skills.Values)
        {
            foreach (var op in skill.Operations)
            {
                var tool = DynamicSkillTool.CreateTool(skill, op, _httpClientFactory);
                dynamicTools.Add(tool);
                _logger.LogInformation("Registered skill tool: {Tool}", tool.Name);
            }
        }

        _toolRegistry.AddTools(dynamicTools);

        _logger.LogInformation("Skill system initialized with {Count} skill tools", dynamicTools.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the hosted service.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

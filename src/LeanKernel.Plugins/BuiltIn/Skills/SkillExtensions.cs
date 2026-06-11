using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Plugins.BuiltIn.Skills;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Plugins;

public static class SkillExtensions
{
    public static IServiceCollection AddLeanKernelSkills(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<SkillParser>();
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

public sealed class SkillHostedService : IHostedService
{
    private readonly RuntimeSkillRegistry _registry;
    private readonly IToolRegistry _toolRegistry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SkillHostedService> _logger;

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

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

using System.ClientModel;

using LeanKernel;
using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Memory;
using LeanKernel.Logic.Providers;
using LeanKernel.Services.Common.Configuration;
using LeanKernel.Services.Learning;
using LeanKernel.Services.Learning.Onboarding;
using LeanKernel.Services.Learning.Scheduler;
using LeanKernel.Services.Learning.Steps;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using OpenAI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OpenAISettings>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<AgentSettings>(builder.Configuration.GetSection("Agents"));
builder.Services.Configure<MemorySettings>(builder.Configuration.GetSection("OpenAI:Memory"));
builder.Services.Configure<FactExtractionSettings>(builder.Configuration.GetSection("OpenAI:FactExtraction"));
builder.Services.Configure<GBrainSettings>(builder.Configuration.GetSection("GBrain"));
builder.Services.Configure<LearningSettings>(builder.Configuration.GetSection("Learning"));
builder.Services.Configure<SchedulerSettings>(builder.Configuration.GetSection("Scheduler"));

builder.Services.AddOptions<LearningSettings>()
    .Validate(settings => settings.TurnQueueCapacity > 0, "TurnQueueCapacity must be > 0")
    .ValidateOnStart();

builder.Services.AddOptions<SchedulerSettings>()
    .Validate(settings => settings.PollIntervalSeconds > 0, "PollIntervalSeconds must be > 0")
    .ValidateOnStart();

builder.Services.AddDbContextFactory<EntityContext>(options =>
{
    var (connectionStringName, connectionString) = builder.Configuration.ResolveConnectionString(
        Constants.ConnectionStrings.All);

    options.ConfigureOptions(
        connectionStringName,
        connectionString,
        builder.Environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase),
        builder.Environment.IsDevelopment(),
        builder.Environment.IsDevelopment());
});

builder.Services.AddMemoryPageServices();
builder.Services.AddScoped<IChannelMemoryPolicyResolver, ChannelMemoryPolicyResolver>();

builder.Services.AddChatClient(sp =>
{
    var cfg = sp.GetRequiredService<IOptions<OpenAISettings>>().Value;
    var agentSettings = sp.GetRequiredService<IOptions<AgentSettings>>().Value;

    var modelId = agentSettings.Tools.Enabled && !string.IsNullOrWhiteSpace(cfg.ToolModel)
        ? cfg.ToolModel
        : cfg.DefaultModel;

    var client = new OpenAIClient(
        new ApiKeyCredential(cfg.ApiKey),
        new OpenAIClientOptions { Endpoint = new Uri(cfg.BaseUrl) });
    return client.GetChatClient(modelId).AsIChatClient();
})
.UseFunctionInvocation()
.UseLogging();

builder.Services.AddKeyedScoped<IChatClient>("small-model", (sp, _) =>
{
    var cfg = sp.GetRequiredService<IOptions<MemorySettings>>().Value;
    if (!cfg.Enabled)
    {
        return new DisabledChatClient();
    }

    var openAi = sp.GetRequiredService<IOptions<OpenAISettings>>().Value;
    var client = new OpenAIClient(
        new ApiKeyCredential(openAi.ApiKey),
        new OpenAIClientOptions { Endpoint = new Uri(openAi.BaseUrl) });
    return client.GetChatClient(cfg.ModelId).AsIChatClient();
});

builder.Services.AddKeyedScoped<IChatClient>("fact-extraction", (sp, _) =>
{
    var cfg = sp.GetRequiredService<IOptions<FactExtractionSettings>>().Value;
    var openAi = sp.GetRequiredService<IOptions<OpenAISettings>>().Value;
    var client = new OpenAIClient(
        new ApiKeyCredential(openAi.ApiKey),
        new OpenAIClientOptions { Endpoint = new Uri(openAi.BaseUrl) });
    return client.GetChatClient(cfg.ModelId).AsIChatClient();
});

builder.Services.AddSingleton(sp =>
{
    var settings = sp.GetRequiredService<IOptions<LearningSettings>>().Value;
    return new TurnEventQueue(settings.TurnQueueCapacity);
});
builder.Services.AddSingleton<ITurnEventProducer>(sp => sp.GetRequiredService<TurnEventQueue>());
builder.Services.AddSingleton<ITurnEventConsumer>(sp => sp.GetRequiredService<TurnEventQueue>());

builder.Services.AddScoped<FactExtractionStep>();
builder.Services.AddScoped<IdentityIntentExtractionStep>();
builder.Services.AddScoped<CapabilityGapDetectionStep>();
builder.Services.AddScoped<EngagementTrackingStep>();
builder.Services.AddScoped<KnowledgePageUpdateCoordinator>();
builder.Services.AddScoped<OnboardingGapDetector>();
builder.Services.AddScoped<OnboardingDirectiveBuilder>();

builder.Services.AddSingleton<CronScheduleEvaluator>();
builder.Services.AddScoped<JobExecutor>();
builder.Services.AddScoped<TimeBoundaryService>();

builder.Services.AddHostedService<LearningBackgroundWorker>();
builder.Services.AddHostedService<SchedulerHostedService>();

var gbrainSettings = builder.Configuration.GetSection("GBrain").Get<GBrainSettings>() ?? new GBrainSettings();
builder.Services.AddGBrainMemory(gbrainSettings);
builder.Services.AddServiceHealthChecks();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<IDbContextFactory<EntityContext>>();
    await using var db = await context.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
}

app.MapHealthChecks(Constants.Healthchecks.Path, new HealthCheckOptions
{
    ResponseWriter = (context, report) =>
    {
        context.Response.ContentType = Constants.ContentTypes.JsonUtf8;
        return context.Response.WriteAsync(report.ToJson());
    },
});

app.Run();
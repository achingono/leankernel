using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Gateway.Configuration;
using LeanKernel.Gateway.Memory;
using LeanKernel.Logic.Providers;
using LeanKernel.Services.Common;
using LeanKernel.Services.Common.Contracts;
using LeanKernel.Services.Common.Queue;
using LeanKernel.Services.Learning.Configuration;
using LeanKernel.Services.Learning.Learning;
using LeanKernel.Services.Learning.Scheduler;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<LearningRuntimeOptions>()
    .BindConfiguration("Agents:Learning")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<SchedulerRuntimeOptions>()
    .BindConfiguration("Agents:Learning:Scheduler")
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.Configure<GBrainSettings>(builder.Configuration.GetSection("GBrain"));

builder.Services.AddEntityContext(options =>
{
    var (connectionStringName, connectionString) = builder.Configuration.ResolveConnectionString([
        "Postgres",
        "SqlServer",
        "Sqlite"
    ]);

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Connection string is missing. Specify any of 'Postgres,SqlServer,Sqlite'.");
    }

    switch (connectionStringName)
    {
        case "SqlServer":
            options.UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure());
            break;
        case "Sqlite":
            options.UseSqlite(connectionString);
            break;
        case "Postgres":
            options.UseNpgsql(connectionString);
            break;
        default:
            throw new InvalidOperationException("Unsupported connection string name. Specify any of 'Postgres,SqlServer,Sqlite'.");
    }

    options
        .EnableDetailedErrors(builder.Environment.IsDevelopment())
        .EnableSensitiveDataLogging(builder.Environment.IsDevelopment());
});

builder.Services.AddSingleton<ITurnEventQueue>(sp =>
{
    var options = sp.GetRequiredService<IOptions<LearningRuntimeOptions>>().Value;
    return new BoundedTurnEventQueue(options.QueueCapacity);
});
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddTransient<GBrainAuthHandler>();
builder.Services.AddHttpClient<GBrainMcpClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IOptions<GBrainSettings>>().Value;
    var baseUrl = config.BaseUrl.TrimEnd('/');
    client.BaseAddress = new Uri($"{baseUrl}/mcp");
    client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
})
.AddHttpMessageHandler<GBrainAuthHandler>();
builder.Services.AddScoped<IGBrainMcpClient>(sp => sp.GetRequiredService<GBrainMcpClient>());
builder.Services.AddScoped<IChannelMemoryPolicyResolver, SingleChannelMemoryPolicyResolver>();
builder.Services.AddScoped<IMemoryClient, GBrainMemoryClient>();
builder.Services.AddScoped<IKnowledgePageUpdateCoordinator, KnowledgePageUpdateCoordinator>();
builder.Services.AddScoped<ILearningPipelineStep, FactExtractionLearningStep>();
builder.Services.AddScoped<ILearningPipelineStep, IdentityIntentLearningStep>();
builder.Services.AddScoped<ILearningPipelineStep, CapabilityGapLearningStep>();
builder.Services.AddScoped<ILearningPipelineStep, EngagementTrackingLearningStep>();
builder.Services.AddScoped<ISelfImprovementPipeline, SelfImprovementPipeline>();
builder.Services.AddScoped<ILearningStepRunner, LearningStepRunner>();
builder.Services.AddScoped<IOnboardingGapDetector, OnboardingGapDetector>();
builder.Services.AddScoped<IOnboardingDirectiveBuilder, OnboardingDirectiveBuilder>();
builder.Services.AddScoped<IOnboardingDirectivePublisher, MemoryOnboardingDirectivePublisher>();
builder.Services.AddScoped<IScheduledJobHandler, PingScheduledJobHandler>();
builder.Services.AddScoped<IScheduledJobHandler, ReplayTurnScheduledJobHandler>();
builder.Services.AddScoped<IScheduledJobHandler, ExecuteLearningStepScheduledJobHandler>();
builder.Services.AddScoped<IScheduledJobHandler, OnboardingGapDetectionScheduledJobHandler>();
builder.Services.AddScoped<IScheduledJobDefinitionProvider, DbScheduledJobDefinitionProvider>();
builder.Services.AddScoped<IScheduledJobExecutor, ScheduledJobExecutor>();
builder.Services.AddHostedService<LearningBackgroundWorker>();
builder.Services.AddHostedService<SchedulerHostedService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost(LearningServiceRoutes.TurnEventsPath,
    async Task<IResult> (CompletedTurnEvent turnEvent, ITurnEventQueue queue, CancellationToken cancellationToken) =>
    {
        var accepted = await queue.EnqueueAsync(turnEvent, cancellationToken).ConfigureAwait(false);
        return accepted
            ? Results.Accepted()
            : Results.StatusCode(StatusCodes.Status429TooManyRequests);
    });

app.Run();

using System.Text.Json;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Events;
using LeanKernel.Services.Learning.Steps;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LeanKernel.Services.Learning;

/// <summary>
/// Background service that processes turn-completed events through the learning pipeline.
/// </summary>
public sealed class LearningBackgroundWorker : BackgroundService
{
    private const string TurnCompletedRecordType = "LeanKernel.Events.TurnCompletedEvent";
    private const string CheckpointName = "learning.turn-completed";

    private readonly ITurnEventConsumer _consumer;
    private readonly ITurnEventProducer _producer;
    private readonly IDbContextFactory<EntityContext> _contextFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<LearningSettings> _settings;
    private readonly ILogger<LearningBackgroundWorker> _logger;
    private DateTime _lastPollAtUtc = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="LearningBackgroundWorker"/> class.
    /// </summary>
    /// <param name="consumer">The turn event consumer.</param>
    /// <param name="producer">The turn event producer.</param>
    /// <param name="contextFactory">The entity-context factory.</param>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="settings">The learning settings.</param>
    /// <param name="logger">The logger.</param>
    public LearningBackgroundWorker(
        ITurnEventConsumer consumer,
        ITurnEventProducer producer,
        IDbContextFactory<EntityContext> contextFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<LearningSettings> settings,
        ILogger<LearningBackgroundWorker> logger)
    {
        _consumer = consumer;
        _producer = producer;
        _contextFactory = contextFactory;
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Learning background worker started");

        await PollAndEnqueueTurnEventsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_settings.Value.Enabled && DateTime.UtcNow - _lastPollAtUtc >= TimeSpan.FromSeconds(2))
                {
                    await PollAndEnqueueTurnEventsAsync(stoppingToken);
                }

                var turnEvent = await _consumer.TryDequeueAsync(stoppingToken);
                if (turnEvent == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }

                await ProcessTurnAsync(turnEvent, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in learning worker loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("Learning background worker stopped");
    }

    private async Task PollAndEnqueueTurnEventsAsync(CancellationToken ct)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);

        var checkpoint = await db.Set<LearningCheckpointEntity>()
            .SingleOrDefaultAsync(c => c.Name == CheckpointName, ct);

        if (checkpoint is null)
        {
            checkpoint = new LearningCheckpointEntity
            {
                Name = CheckpointName,
            };

            db.Set<LearningCheckpointEntity>().Add(checkpoint);
            await db.SaveChangesAsync(ct);
        }

        var query = db.Events
            .AsNoTracking()
            .Where(e => e.RecordType == TurnCompletedRecordType);

        if (checkpoint.LastProcessedCreatedOnUtc is { } lastCreated)
        {
            var lastEventRowId = checkpoint.LastProcessedEventRowId;
            query = query.Where(e => e.CreatedOn > lastCreated
                || (e.CreatedOn == lastCreated && lastEventRowId.HasValue && e.Id != lastEventRowId.Value));
        }

        var replayBatch = await query
            .OrderBy(e => e.CreatedOn)
            .ThenBy(e => e.Id)
            .Take(200)
            .ToListAsync(ct);

        foreach (var eventRow in replayBatch)
        {
            TurnCompletedEvent? turnEvent;
            try
            {
                turnEvent = JsonSerializer.Deserialize<TurnCompletedEvent>(eventRow.PayloadJson, Constants.Serialization.JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Skipping malformed TurnCompletedEvent payload for row {EventRowId}", eventRow.Id);
                continue;
            }

            if (turnEvent is null)
            {
                continue;
            }

            await _producer.EnqueueAsync(turnEvent, ct);
            checkpoint.LastProcessedCreatedOnUtc = eventRow.CreatedOn;
            checkpoint.LastProcessedEventRowId = eventRow.Id;
            checkpoint.UpdatedAt = DateTime.UtcNow;
        }

        if (replayBatch.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogDebug("Queued {Count} replayed turn-completed events", replayBatch.Count);
        }

        _lastPollAtUtc = DateTime.UtcNow;
    }

    private async Task ProcessTurnAsync(LeanKernel.Events.TurnCompletedEvent turnEvent, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        try
        {
            var factStep = scope.ServiceProvider.GetRequiredService<FactExtractionStep>();
            var facts = await factStep.ExecuteAsync(turnEvent, ct);

            var intentStep = scope.ServiceProvider.GetRequiredService<IdentityIntentExtractionStep>();
            await intentStep.ExecuteAsync(turnEvent, ct);

            var gapStep = scope.ServiceProvider.GetRequiredService<CapabilityGapDetectionStep>();
            await gapStep.ExecuteAsync(turnEvent, ct);

            var engagementStep = scope.ServiceProvider.GetRequiredService<EngagementTrackingStep>();
            await engagementStep.ExecuteAsync(turnEvent, ct);

            if (facts.Count > 0)
            {
                var coordinator = scope.ServiceProvider.GetRequiredService<KnowledgePageUpdateCoordinator>();
                var scopeKey = $"{turnEvent.Envelope.TenantId}/{turnEvent.Envelope.UserId}";
                await coordinator.WriteFactsAsync(scopeKey, facts, ct);
            }

            _logger.LogDebug("Processed turn {TurnId} with {FactCount} facts", turnEvent.TurnId, facts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process turn {TurnId}", turnEvent.TurnId);
        }
    }
}
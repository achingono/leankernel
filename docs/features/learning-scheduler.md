# Learning Scheduler

- `Payload` is stored as text in `ScheduledJobs` and must be valid JSON text.
Phase 07 adds a cron-driven scheduler in `LeanKernel.Services.Learning`.
Operators define jobs in the `ScheduledJobs` table.
Each job is scoped at tenant level, channel level, or both (`TenantId`, `ChannelId`).
At least one scope value is required.

## Job Types

### `learning.ping`

Purpose: heartbeat/diagnostic job.

Payload schema:

```json
{
  "message": "optional string"
}
```

Example job:

```json
{
  "Name": "learning-heartbeat",
  "Cron": "*/5 * * * *",
  "Enabled": true,
  "JobType": "learning.ping",
  "Payload": "{\"message\":\"scheduler is alive\"}"
}
```

### `learning.replay-turn`

Purpose: replay one completed turn through the full self-improvement pipeline
(fact extraction, identity intent, capability gaps, engagement tracking).

Payload schema:

```json
{
  "tenantId": "guid",
  "userId": "guid",
  "personId": "guid",
  "channelId": "guid",
  "sessionId": "optional string",
  "turnId": "string",
  "recordedAt": "2026-07-19T20:41:00Z",
  "requestMessages": [
    {
      "role": "user",
      "text": "string",
      "createdAt": "optional datetime",
      "authorName": "optional string"
    }
  ],
  "responseMessages": [
    {
      "role": "assistant",
      "text": "string",
      "createdAt": "optional datetime",
      "authorName": "optional string"
    }
  ]
}
```

Example job:

```json
{
  "Name": "replay-sample-turn",
  "Cron": "0 * * * *",
  "Enabled": false,
  "JobType": "learning.replay-turn",
  "Payload": "{\"tenantId\":\"11111111-1111-1111-1111-111111111111\",\"userId\":\"22222222-2222-2222-2222-222222222222\",\"personId\":\"33333333-3333-3333-3333-333333333333\",\"channelId\":\"44444444-4444-4444-4444-444444444444\",\"sessionId\":\"session-1\",\"turnId\":\"turn-1\",\"recordedAt\":\"2026-07-19T20:41:00Z\",\"requestMessages\":[{\"role\":\"user\",\"text\":\"My email is ada@example.com\"}],\"responseMessages\":[{\"role\":\"assistant\",\"text\":\"Thanks, I will remember that.\"}]}"
}
```

### `learning.execute-step`

Purpose: run one specific learning step for one completed turn.

Allowed `stepName` values:

- `fact-extraction`
- `identity-intent`
- `capability-gap`
- `engagement-tracking`

Payload schema:

```json
{
  "stepName": "identity-intent",
  "turnEvent": {
    "tenantId": "guid",
    "userId": "guid",
    "personId": "guid",
    "channelId": "guid",
    "sessionId": "optional string",
    "turnId": "string",
    "recordedAt": "datetime",
    "requestMessages": [],
    "responseMessages": []
  }
}
```

Example job:

```json
{
  "Name": "identity-intent-step",
  "Cron": "15 * * * *",
  "Enabled": false,
  "JobType": "learning.execute-step",
  "Payload": "{\"stepName\":\"identity-intent\",\"turnEvent\":{\"tenantId\":\"11111111-1111-1111-1111-111111111111\",\"userId\":\"22222222-2222-2222-2222-222222222222\",\"personId\":\"33333333-3333-3333-3333-333333333333\",\"channelId\":\"44444444-4444-4444-4444-444444444444\",\"sessionId\":\"session-2\",\"turnId\":\"turn-2\",\"recordedAt\":\"2026-07-19T20:41:00Z\",\"requestMessages\":[{\"role\":\"user\",\"text\":\"You can call me Ada\"}],\"responseMessages\":[{\"role\":\"assistant\",\"text\":\"Got it\"}]}}"
}
```

### `onboarding.detect-gaps`

Purpose: run onboarding-gap detection and publish onboarding directives.

Payload schema: same as `learning.replay-turn` (`CompletedTurnEvent`).

Example job:

```json
{
  "Name": "onboarding-gap-check",
  "Cron": "30 * * * *",
  "Enabled": false,
  "JobType": "onboarding.detect-gaps",
  "Payload": "{\"tenantId\":\"11111111-1111-1111-1111-111111111111\",\"userId\":\"22222222-2222-2222-2222-222222222222\",\"personId\":\"33333333-3333-3333-3333-333333333333\",\"channelId\":\"44444444-4444-4444-4444-444444444444\",\"sessionId\":\"session-3\",\"turnId\":\"turn-3\",\"recordedAt\":\"2026-07-19T20:41:00Z\",\"requestMessages\":[{\"role\":\"user\",\"text\":\"Hi there\"}],\"responseMessages\":[{\"role\":\"assistant\",\"text\":\"Hello\"}]}"
}
```

## Operator Notes

- `Payload` is stored as a string in appsettings and must be valid JSON text.
- Invalid payload JSON causes the scheduled job to fail fast with a clear error.
- Unknown `JobType` values fail fast and list supported types in logs.
- Jobs are evaluated in UTC.
- Job names are unique within scope:
  - `(TenantId, ChannelId, Name)` for tenant+channel jobs
  - `(TenantId, Name)` for tenant-only jobs
  - `(ChannelId, Name)` for channel-only jobs
## Operator Notes

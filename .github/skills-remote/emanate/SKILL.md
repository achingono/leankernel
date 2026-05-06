---
name: emanate
description: "Emanate social media automation API: create, schedule, and publish posts across Twitter/X, LinkedIn, Facebook, and Reddit. Use when: (1) drafting or generating social media posts, (2) scheduling posts for optimal engagement times, (3) publishing posts immediately, (4) checking post status or engagement analytics. NOT for: direct browser-based social media interactions, managing platform login sessions, or modifying engagement automation schedules."
metadata:
  {
    "LeanKernel":
      {
        "emoji": "📢",
      },
  }
---

# Emanate

Emanate is a social media automation platform. Use its REST API to create, schedule, and publish posts across configured social platforms (Twitter/X, LinkedIn, Facebook, Reddit).

## API Basics

Base URL: `http://host.docker.internal:3000`

All requests use JSON. No authentication is required.

```bash
curl -s http://host.docker.internal:3000/api/health
```

## Workflow

The typical post lifecycle is: **draft → schedule → published**

1. List available platforms
2. Create a draft post (manual or AI-generated)
3. Optionally check optimal posting windows
4. Schedule the post (or publish immediately)

## Platforms

List configured platforms:

```bash
curl -s http://host.docker.internal:3000/api/platforms | jq
```

Get a specific platform:

```bash
curl -s http://host.docker.internal:3000/api/platforms/{platform_id} | jq
```

## Creating Posts

### Create a manual draft

```bash
curl -s -X POST http://host.docker.internal:3000/api/posts \
  -H "Content-Type: application/json" \
  -d '{
    "platform_id": "PLATFORM_ID",
    "content": "Your post content here",
    "content_type": "text"
  }' | jq
```

Fields:
- `platform_id` (required) — ID from `/api/platforms`
- `content` (required) — the post text
- `content_type` (optional) — `"text"` (default), `"thread"`, `"poll"`, `"article"`

### Generate a post with AI

```bash
curl -s -X POST http://host.docker.internal:3000/api/posts/generate \
  -H "Content-Type: application/json" \
  -d '{
    "platform_id": "PLATFORM_ID",
    "prompt": "Write a post about the latest trends in AI",
    "content_type": "text"
  }' | jq
```

Fields:
- `platform_id` (required) — target platform (voice/style adapts to platform)
- `prompt` (required) — instructions for what the post should be about
- `content_type` (optional) — `"text"`, `"thread"`, `"poll"`, `"article"`
- `model` (optional) — LLM model to use (defaults to platform config)

## Scheduling Posts

### Check optimal posting windows

```bash
# Get suggested time slots for a platform type
curl -s "http://host.docker.internal:3000/api/optimal-windows/suggest?platform_type=linkedin&count=3" | jq
```

Platform types: `twitter`, `linkedin`, `facebook`, `reddit`

### Schedule a draft post

```bash
curl -s -X POST http://host.docker.internal:3000/api/posts/{post_id}/schedule \
  -H "Content-Type: application/json" \
  -d '{
    "scheduled_at": "2025-01-15T14:30:00Z",
    "timezone": "America/Edmonton"
  }' | jq
```

Fields:
- `scheduled_at` (required) — ISO 8601 datetime
- `timezone` (optional) — IANA timezone, defaults to UTC
- `window_source` (optional) — source for optimal window selection
- `jitter_max_ms` (optional) — random delay in ms to humanize posting time

### Publish immediately

```bash
curl -s -X POST http://host.docker.internal:3000/api/posts/{post_id}/publish-now | jq
```

## Managing Posts

### List posts

```bash
# All posts
curl -s http://host.docker.internal:3000/api/posts | jq

# Filter by status: draft, scheduled, publishing, published, failed
curl -s "http://host.docker.internal:3000/api/posts?status=draft" | jq

# Filter by platform
curl -s "http://host.docker.internal:3000/api/posts?platform_id=PLATFORM_ID" | jq

# Pagination
curl -s "http://host.docker.internal:3000/api/posts?limit=10&offset=0" | jq
```

### Get a specific post

```bash
curl -s http://host.docker.internal:3000/api/posts/{post_id} | jq
```

### Edit a draft

```bash
curl -s -X PATCH http://host.docker.internal:3000/api/posts/{post_id} \
  -H "Content-Type: application/json" \
  -d '{
    "content": "Updated post content"
  }' | jq
```

### Delete a post

Only `draft` or `failed` posts can be deleted:

```bash
curl -s -X DELETE http://host.docker.internal:3000/api/posts/{post_id} | jq
```

## Analytics & Logs

### Engagement summary

```bash
# Daily engagement summary for the last 7 days
curl -s "http://host.docker.internal:3000/api/logs/summary?days=7" | jq
```

### List recent automation runs

```bash
curl -s "http://host.docker.internal:3000/api/logs/runs?limit=10" | jq
```

### Get engagements from a run

```bash
curl -s http://host.docker.internal:3000/api/logs/runs/{run_id}/engagements | jq
```

## Schedules (Recurring Automation)

These are cron-based automation schedules that control when engagement runs happen (separate from one-off post scheduling above).

### List schedules

```bash
curl -s http://host.docker.internal:3000/api/schedules | jq
```

### Get schedule details

```bash
curl -s http://host.docker.internal:3000/api/schedules/{schedule_id} | jq
```

## Important Notes

- Always list platforms first (`GET /api/platforms`) to get valid `platform_id` values
- Post generation uses the platform's configured voice/style — you only need to provide the topic
- When scheduling, use `GET /api/optimal-windows/suggest` to pick the best posting time
- The `timezone` field in scheduling should match the user's timezone (default: UTC)
- Posts go through the lifecycle: draft → scheduled → publishing → published (or failed)
- Only draft or failed posts can be deleted; scheduled posts must be published or will auto-publish at the scheduled time

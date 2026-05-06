---
name: emanate
description: "Emanate social media automation API: create, schedule, and publish posts across Twitter/X, LinkedIn, Facebook, and Reddit. Use when: (1) drafting or generating social media posts, (2) scheduling posts for optimal engagement times, (3) publishing posts immediately, (4) checking post status or engagement analytics. NOT for: direct browser-based social media interactions, managing platform login sessions, or modifying engagement automation schedules."
metadata:
  emoji: "📢"
  homepage: "https://emanate.ca"
  category: social
  tags: [social-media, marketing, automation, posts]
runtime:
  type: http
  baseUrl: "http://host.docker.internal:3000"
  auth:
    type: none
  requires:
    bins: []
  egress:
    allowHosts:
      - "host.docker.internal:3000"
operations:
  - id: health
    summary: "Check Emanate API health status."
    invoke:
      httpMethod: GET
      httpPath: "/api/health"
    parameters:
      type: object
      properties: {}
      additionalProperties: false
  - id: list_platforms
    summary: "List all configured social media platforms."
    invoke:
      httpMethod: GET
      httpPath: "/api/platforms"
    parameters:
      type: object
      properties: {}
      additionalProperties: false
  - id: get_platform
    summary: "Get details of a specific platform."
    invoke:
      httpMethod: GET
      httpPath: "/api/platforms/{platform_id}"
    parameters:
      type: object
      properties:
        platform_id:
          type: string
          description: "Platform ID"
      required: [platform_id]
      additionalProperties: false
  - id: list_posts
    summary: "List posts with optional filtering by status or platform."
    invoke:
      httpMethod: GET
      httpPath: "/api/posts"
      flags:
        status: "status"
        platform_id: "platform_id"
        limit: "limit"
        offset: "offset"
    parameters:
      type: object
      properties:
        status:
          type: string
          enum: [draft, scheduled, publishing, published, failed]
          description: "Filter by post status"
        platform_id:
          type: string
          description: "Filter by platform ID"
        limit:
          type: integer
          description: "Results limit (default 20)"
        offset:
          type: integer
          description: "Pagination offset (default 0)"
      additionalProperties: false
  - id: get_post
    summary: "Get details of a specific post."
    invoke:
      httpMethod: GET
      httpPath: "/api/posts/{post_id}"
    parameters:
      type: object
      properties:
        post_id:
          type: string
          description: "Post ID"
      required: [post_id]
      additionalProperties: false
  - id: create_post
    summary: "Create a draft post manually."
    invoke:
      httpMethod: POST
      httpPath: "/api/posts"
    parameters:
      type: object
      properties:
        platform_id:
          type: string
          description: "Target platform ID"
        content:
          type: string
          description: "Post content"
        content_type:
          type: string
          enum: [text, thread, poll, article]
          description: "Content type (default: text)"
      required: [platform_id, content]
      additionalProperties: false
  - id: generate_post
    summary: "Generate a post with AI."
    invoke:
      httpMethod: POST
      httpPath: "/api/posts/generate"
    parameters:
      type: object
      properties:
        platform_id:
          type: string
          description: "Target platform ID"
        prompt:
          type: string
          description: "Instructions for post generation"
        content_type:
          type: string
          enum: [text, thread, poll, article]
          description: "Content type (default: text)"
        model:
          type: string
          description: "LLM model to use (optional)"
      required: [platform_id, prompt]
      additionalProperties: false
  - id: edit_post
    summary: "Edit a draft post."
    invoke:
      httpMethod: PATCH
      httpPath: "/api/posts/{post_id}"
    parameters:
      type: object
      properties:
        post_id:
          type: string
          description: "Post ID to edit"
        content:
          type: string
          description: "Updated post content"
      required: [post_id]
      additionalProperties: false
  - id: delete_post
    summary: "Delete a draft or failed post."
    invoke:
      httpMethod: DELETE
      httpPath: "/api/posts/{post_id}"
    parameters:
      type: object
      properties:
        post_id:
          type: string
          description: "Post ID to delete"
      required: [post_id]
      additionalProperties: false
  - id: schedule_post
    summary: "Schedule a draft post for publication."
    invoke:
      httpMethod: POST
      httpPath: "/api/posts/{post_id}/schedule"
    parameters:
      type: object
      properties:
        post_id:
          type: string
          description: "Post ID to schedule"
        scheduled_at:
          type: string
          format: date-time
          description: "Scheduled publish time (ISO 8601)"
        timezone:
          type: string
          description: "IANA timezone (default: UTC)"
        jitter_max_ms:
          type: integer
          description: "Random delay in milliseconds to humanize posting"
      required: [post_id, scheduled_at]
      additionalProperties: false
  - id: publish_now
    summary: "Publish a draft post immediately."
    invoke:
      httpMethod: POST
      httpPath: "/api/posts/{post_id}/publish-now"
    parameters:
      type: object
      properties:
        post_id:
          type: string
          description: "Post ID to publish"
      required: [post_id]
      additionalProperties: false
  - id: optimal_windows
    summary: "Get optimal posting times for a platform."
    invoke:
      httpMethod: GET
      httpPath: "/api/optimal-windows/suggest"
      flags:
        platform_type: "platform_type"
        count: "count"
    parameters:
      type: object
      properties:
        platform_type:
          type: string
          enum: [twitter, linkedin, facebook, reddit]
          description: "Platform type"
        count:
          type: integer
          description: "Number of suggested windows (default 3)"
      required: [platform_type]
      additionalProperties: false
  - id: engagement_summary
    summary: "Get daily engagement summary for recent posts."
    invoke:
      httpMethod: GET
      httpPath: "/api/logs/summary"
      flags:
        days: "days"
    parameters:
      type: object
      properties:
        days:
          type: integer
          description: "Number of days to summarize (default 7)"
      additionalProperties: false
---

# Emanate

Emanate is a social media automation platform. Use its REST API to create, schedule, and publish posts across configured social platforms (Twitter/X, LinkedIn, Facebook, Reddit).

## API Basics

Base URL: `http://192.168.1.41:3000`

All requests use JSON. No authentication is required.

```bash
curl -s http://192.168.1.41:3000/api/health
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
curl -s http://192.168.1.41:3000/api/platforms | jq
```

Get a specific platform:

```bash
curl -s http://192.168.1.41:3000/api/platforms/{platform_id} | jq
```

## Creating Posts

### Create a manual draft

```bash
curl -s -X POST http://192.168.1.41:3000/api/posts \
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
curl -s -X POST http://192.168.1.41:3000/api/posts/generate \
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
curl -s "http://192.168.1.41:3000/api/optimal-windows/suggest?platform_type=linkedin&count=3" | jq
```

Platform types: `twitter`, `linkedin`, `facebook`, `reddit`

### Schedule a draft post

```bash
curl -s -X POST http://192.168.1.41:3000/api/posts/{post_id}/schedule \
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
curl -s -X POST http://192.168.1.41:3000/api/posts/{post_id}/publish-now | jq
```

## Managing Posts

### List posts

```bash
# All posts
curl -s http://192.168.1.41:3000/api/posts | jq

# Filter by status: draft, scheduled, publishing, published, failed
curl -s "http://192.168.1.41:3000/api/posts?status=draft" | jq

# Filter by platform
curl -s "http://192.168.1.41:3000/api/posts?platform_id=PLATFORM_ID" | jq

# Pagination
curl -s "http://192.168.1.41:3000/api/posts?limit=10&offset=0" | jq
```

### Get a specific post

```bash
curl -s http://192.168.1.41:3000/api/posts/{post_id} | jq
```

### Edit a draft

```bash
curl -s -X PATCH http://192.168.1.41:3000/api/posts/{post_id} \
  -H "Content-Type: application/json" \
  -d '{
    "content": "Updated post content"
  }' | jq
```

### Delete a post

Only `draft` or `failed` posts can be deleted:

```bash
curl -s -X DELETE http://192.168.1.41:3000/api/posts/{post_id} | jq
```

## Analytics & Logs

### Engagement summary

```bash
# Daily engagement summary for the last 7 days
curl -s "http://192.168.1.41:3000/api/logs/summary?days=7" | jq
```

### List recent automation runs

```bash
curl -s "http://192.168.1.41:3000/api/logs/runs?limit=10" | jq
```

### Get engagements from a run

```bash
curl -s http://192.168.1.41:3000/api/logs/runs/{run_id}/engagements | jq
```

## Schedules (Recurring Automation)

These are cron-based automation schedules that control when engagement runs happen (separate from one-off post scheduling above).

### List schedules

```bash
curl -s http://192.168.1.41:3000/api/schedules | jq
```

### Get schedule details

```bash
curl -s http://192.168.1.41:3000/api/schedules/{schedule_id} | jq
```

## Important Notes

- Always list platforms first (`GET /api/platforms`) to get valid `platform_id` values
- Post generation uses the platform's configured voice/style — you only need to provide the topic
- When scheduling, use `GET /api/optimal-windows/suggest` to pick the best posting time
- The `timezone` field in scheduling should match the user's timezone (default: UTC)
- Posts go through the lifecycle: draft → scheduled → publishing → published (or failed)
- Only draft or failed posts can be deleted; scheduled posts must be published or will auto-publish at the scheduled time

---
name: ms-todo
description: "Manage Microsoft To Do list groups, lists, tasks, attachments, and checklist steps with ms-todo-cli. Use when a user wants you to capture follow-ups, create actionable reminders, review task lists, search tasks by keyword, or complete/update Microsoft To Do items that require their action. NOT for Outlook email, Outlook calendar, or general Microsoft Graph tasks outside To Do."
metadata:
  {
    "emoji": "✅",
    "homepage": "https://github.com/achingono/ms-todo-cli",
    "requires": { "bins": ["ms-todo-cli"] },
  }
---

# Microsoft To Do

Use `ms-todo-cli` to manage Microsoft To Do list groups, lists, tasks, attachments, and checklist steps.

## Before doing anything

1. Check auth first:

```bash
ms-todo-cli auth status
```

2. If the CLI says `MS_TODO_CLIENT_ID` is missing, stop and tell the user the gateway environment needs that variable set before the skill can be used.
3. If the CLI says login is required, ask the user to complete the interactive device-code flow with:

```bash
ms-todo-cli auth login
```

4. If `ms-todo-cli auth login` fails with `invalid_grant`, treat that as an app-registration problem, not a temporary login error. The configured client ID must point at a public-client Azure app registration that supports the CLI's device-code flow.

After login succeeds, re-run `ms-todo-cli auth status`.

5. Run feature capability probe once per session and cache the result in your working notes:

```bash
ms-todo-capability-probe | jq
```

Use the probe to decide whether to attempt `group`, `task search`, and `task attach` operations for this tenant.

## Capability probe and fallbacks

Treat probe status values like this:
- `supported`: feature can be used normally.
- `unsupported`: skip that feature and use fallback workflow.
- `requires_context`: feature needs a real task/list context before it can be confirmed.

If probe cannot run, use runtime error handling below.

### Group fallback rules

If group operations return `Resource not found for the segment 'listGroups'`:
- treat list groups as unsupported for this tenant
- continue with list-only organization
- ask whether to use an existing list or create a new list with a clear naming prefix (for example `Work - ...`)
- do not retry group commands in the same session unless user explicitly asks

### Search fallback rules

If `task search --query` returns `Filter not supported`:
- treat cross-list keyword search as unsupported for this tenant
- fallback to list-by-list search using:

```bash
ms-todo-cli list list | jq
ms-todo-cli task list --list-id "LIST_ID" | jq
```

- check likely candidate lists first (`Tasks`, `Inbox`, `To Do`, `Action Items`, `Flagged Emails`)
- report that results were derived from iterative list scans

### Attach fallback rules

If `task attach` returns `Invalid request` (or task-not-found style errors):
- first validate that `task-id`, `list-id`, and file path are correct
- if IDs are missing, fetch/resolve them and retry once
- if request still fails, continue by storing file path or summary text in task notes instead of attachment upload
- tell the user attachment upload was not available in the current context and that notes fallback was used

## General workflow

When the user asks you to track something they need to do:

1. Inspect the available lists first:

```bash
ms-todo-cli list list | jq
```

2. Prefer an existing obvious catch-all list if one already exists (for example a list with a name like `Tasks`, `Inbox`, `Todo`, `To Do`, `Action Items`, or similar).
3. If no suitable list exists and the user did not tell you which list to use, ask before creating a new list.
4. Avoid duplicates by listing tasks in the target list before creating a new one when the request looks similar to an existing task.
5. Prefer probe-driven behavior: do not call unsupported features repeatedly in the same session.

## Common commands

### Manage list groups

```bash
ms-todo-cli group list | jq
ms-todo-cli group create "Group Name"
ms-todo-cli group update --group-id "GROUP_ID" --name "Renamed Group"
ms-todo-cli group delete --group-id "GROUP_ID"
```

When creating a new list for organization, ask whether it should be placed under an existing group first.
If probe says group is unsupported, skip group commands and continue with list-only fallback.

### Create a list

```bash
ms-todo-cli list create "List Name"
```

### List tasks in a list

```bash
ms-todo-cli task list --list "List Name" | jq
```

### Create a task

```bash
ms-todo-cli task create \
  --list "List Name" \
  --title "Action-oriented task title" \
  --due "2026-04-10T21:00:00Z" \
  --priority high
```

Use:
- `--title` for a clear action the user must take
- `--due` when the user provided a date/time or a deadline can be confidently inferred
- `--priority` only when urgency is explicit

### Search tasks across lists

```bash
ms-todo-cli task search --query "keyword" | jq
```

Use search before creating a possibly duplicate task when the title wording is uncertain.
If probe says search is unsupported, use list-by-list fallback scanning.

### Attach a file to a task

```bash
ms-todo-cli task attach --task-id "TASK_ID" --list-id "LIST_ID" --file ./path/to/file
```

Attachment constraints:
- Graph simple attachments only
- max size is 3 MiB (3,145,728 bytes) per file
- ask before uploading sensitive files
If probe says attach is `requires_context` or runtime returns `Invalid request`, validate IDs/path, retry once, then fallback to notes.

### Get a task

```bash
ms-todo-cli task get --task-id "TASK_ID" --list-id "LIST_ID" | jq
```

### Update a task

```bash
ms-todo-cli task update \
  --task-id "TASK_ID" \
  --list-id "LIST_ID" \
  --title "Updated title"
```

### Complete a task

```bash
ms-todo-cli task complete --task-id "TASK_ID" --list-id "LIST_ID"
```

### Manage checklist steps

```bash
ms-todo-cli step list --task-id "TASK_ID" --list-id "LIST_ID" | jq
ms-todo-cli step create --task-id "TASK_ID" --list-id "LIST_ID" --title "Step title"
ms-todo-cli step complete --task-id "TASK_ID" --list-id "LIST_ID" --step-id "STEP_ID"
```

Use steps for multi-part follow-ups like "gather documents", "review draft", or "submit form".

## Response style

When you create or update a Microsoft To Do item, report back with:
- the list used
- the task title
- the due date if one was set
- any checklist steps you created

If the request is ambiguous about ownership, due date, or target list, clarify rather than guessing.

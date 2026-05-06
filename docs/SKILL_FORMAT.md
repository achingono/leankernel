# Skill Definition Format

Skills are defined in markdown files named `SKILL.md` with YAML frontmatter. This format allows users to add new skills without recompiling the container.

## File Structure

Each skill lives in its own directory with a single `SKILL.md` file:

```
skills/
├── emanate/
│   └── SKILL.md
├── doughray/
│   └── SKILL.md
└── my-new-skill/
    └── SKILL.md
```

## SKILL.md Format

### Frontmatter (YAML)

The file starts with YAML frontmatter enclosed in `---` markers:

```yaml
---
name: skill_name
description: "Short description of what this skill does"
metadata:
  LeanKernel:
    emoji: "📢"
    homepage: "https://example.com"
    baseUrl: "http://host.docker.internal:3000"
    cliCommand: "my-cli-tool"
    authType: "none"
---
```

#### Frontmatter Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | ✓ | Skill identifier (lowercase, no spaces) |
| `description` | string | ✓ | What the skill does (used in agent routing) |
| `metadata.LeanKernel.emoji` | string | | Unicode emoji for UI display |
| `metadata.LeanKernel.homepage` | string | | URL to skill documentation |
| `metadata.LeanKernel.baseUrl` | string | | Base URL for HTTP operations |
| `metadata.LeanKernel.cliCommand` | string | | CLI command name for CLI operations |
| `metadata.LeanKernel.authType` | string | | Authentication: `none`, `bearer`, `api_key` |

### Markdown Documentation

After the frontmatter, document your skill's operations and API endpoints.

#### Operation Sections

Each operation is documented as a level-3 heading (`###`):

```markdown
### List Platforms

Description of what this operation does.

**Parameters** (required):
- `platform_id` — ID from `/api/platforms`

**Parameters** (optional):
- `limit` — Maximum results (default: 10)

**Example**:
```bash
curl -s http://host.docker.internal:3000/api/platforms | jq
```

#### HTTP Operations

For HTTP-based skills, document endpoints:

```markdown
### Create Draft Post

Create a new draft post.

**Parameters** (required):
- `platform_id` — Target platform
- `content` — Post text

**Example**:
```bash
curl -X POST http://host.docker.internal:3000/api/posts \
  -H "Content-Type: application/json" \
  -d '{"platform_id": "twitter", "content": "Hello world"}'
```
```

#### CLI Operations

For CLI-based skills, document commands:

```markdown
### List Accounts

Show all linked accounts.

**Example**:
```bash
simplefin-cli accounts list
```
```

## Complete Example

```yaml
---
name: weather_api
description: "Get weather forecasts and conditions for any location using Weather API"
metadata:
  LeanKernel:
    emoji: "⛅"
    homepage: "https://weatherapi.com"
    baseUrl: "https://api.weatherapi.com/v1"
    authType: "api_key"
---

# Weather API

Real-time weather data and forecasts from WeatherAPI.

## Current Weather

### Get Current Conditions

Get the current weather for a location.

**Parameters** (required):
- `location` — City name or coordinates
- `aqi` — Include air quality (yes/no)

**Example**:
```bash
curl "https://api.weatherapi.com/v1/current.json?key=YOUR_KEY&q=London&aqi=yes" | jq
```

### Get 7-Day Forecast

Get a 7-day weather forecast.

**Parameters** (required):
- `location` — City name or coordinates

**Parameters** (optional):
- `days` — Number of days (1-10, default: 1)
- `aqi` — Include air quality (yes/no)

**Example**:
```bash
curl "https://api.weatherapi.com/v1/forecast.json?key=YOUR_KEY&q=London&days=7" | jq
```
```

## Skill Discovery

Skills are automatically discovered from configured directories at startup:
- `LeanKernel:Skills:BasePaths` in `appsettings.json` (comma-separated)
- Default: `/app/data/skills` and `/app/.github/skills-remote`

Changes to SKILL.md files are watched and automatically reloaded (no restart needed).

## Runtime Behavior

The skill system loads skills using:

1. **Parser** (`SkillParser`) — Extracts frontmatter and operations from SKILL.md
2. **Registry** (`RuntimeSkillRegistry`) — Discovers and caches skill definitions
3. **Factory** (`DynamicSkillToolFactory`) — Creates tool instances from definitions
4. **Tool** (`DynamicSkillTool`) — Executes HTTP or CLI operations at runtime

When an agent calls a skill, the `DynamicSkillTool`:

1. Looks up the operation in the skill definition
2. Builds the HTTP request or CLI command
3. Executes it and returns the result

### Supported Operation Types

- **HTTP**: Calls REST endpoints with JSON payloads
- **CLI**: Executes command-line tools with arguments
- **Composite**: Tries HTTP first, falls back to CLI

## Adding a New Skill

1. Create a directory: `skills/my-new-skill/`
2. Create `SKILL.md` with frontmatter and documentation
3. Place in a watched directory (e.g., `/app/data/skills/`)
4. Restart the container (or wait for file watcher to reload)
5. Agents can now use the skill!

No code changes or recompilation needed.

## Debugging

To verify a skill loaded correctly:

1. Check logs for "Loaded skill: {SkillName}"
2. Query the skill registry via the admin API (if exposed)
3. Check the skill directory is in `LeanKernel:Skills:BasePaths`
4. Verify SKILL.md frontmatter is valid YAML

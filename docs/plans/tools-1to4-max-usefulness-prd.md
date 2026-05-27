# Tools 1-4 maximally-useful built-ins PRD

## Summary
Implement four high-leverage built-in tools so the agent can act on APIs, transform structured payloads, handle tabular files, and query configured databases safely by default:

1. `http_request`
2. `json_transform`
3. `csv_xlsx_read_write`
4. `database_query`

## Goals
- Add production-usable built-in tool contracts for the four capabilities.
- Keep tools deterministic, bounded, and safe (SSRF, path, SQL, payload-size controls).
- Reuse existing patterns in `LeanKernel.Tools` (static tool creator + `ToolDefinition` + scoped DI resolution).
- Add focused unit coverage for success, validation, and failure branches.
- Register tools in default built-in registry.

## Non-goals
- No general scripting runtime.
- No arbitrary network access to private/local hosts.
- No unconstrained SQL execution.
- No streaming or chunked tool protocol extensions.

## Architecture and placement
- `src/LeanKernel.Tools/BuiltIn/Internet/HttpRequestTool.cs`
- `src/LeanKernel.Tools/BuiltIn/Data/JsonTransformTool.cs`
- `src/LeanKernel.Tools/BuiltIn/Data/CsvXlsxReadWriteTool.cs`
- `src/LeanKernel.Tools/BuiltIn/Data/DatabaseQueryTool.cs`
- `src/LeanKernel.Tools/BuiltIn/Common/` for shared helper additions where needed.
- Register all tools in `ToolsServiceCollectionExtensions`.

## Contract and parser constraints
- `ToolParameter` currently supports flat metadata only (`name`, `type`, `description`, `required`), so nested objects/arrays are documented via `Type = "object"` / `Type = "array"` and precise field-level descriptions.
- Add `ToolArgumentReader` helpers for composite arguments:
  - `GetJsonObjectOrNull(...)`
  - `GetJsonArrayOrNull(...)`
  - `GetStringDictionary(...)`
- Tools should parse object/array/map arguments through those helpers to keep behavior consistent across tracks.

## Configuration model changes
Add a new configuration section under `LeanKernel`:

- `DatabaseQuery` (new `DatabaseQueryConfig` type; avoid collision with existing `DatabaseConfig`):
  - `MaxRows` (default 200)
  - `DefaultTimeoutSeconds` (default 30)
  - `Connections` (named connection definitions)
    - `Name`
    - `Provider` (`postgres`, `sqlite`)
    - `ConnectionString`
    - `ReadOnly` (default true)
    - `AllowedSchemas` (postgres only; optional)

And bind in:
- `src/LeanKernel.Abstractions/Configuration/LeanKernelConfig.cs`
- `src/LeanKernel.Gateway/appsettings.json`
- `src/LeanKernel.Gateway/appsettings.Development.json`

## Tool designs

### 1) `http_request`
**Category:** `internet`  
**Description:** Perform a bounded HTTP request with headers/query/body and return status, headers, and truncated response content.

**Parameters**
- `url` (required string)
- `method` (optional string; default `GET`; allow `GET|POST|PUT|PATCH|DELETE|HEAD`)
- `headers` (optional object/string map)
- `query` (optional object/string map)
- `body` (optional string/object; object serialized to JSON)
- `content_type` (optional string; default `application/json` when body is object)
- `max_output_chars` (optional int; clamped, max 20000)
- `follow_redirects` (optional bool; default true, max 3 hops)

**Output**
- JSON string with:
  - `statusCode`
  - `reasonPhrase`
  - `responseHeaders` (subset)
  - `contentType`
  - `content` (truncated)
  - `truncated` (bool)

**Safety**
- Reuse URL validation/SSRF checks from current `web_fetch` policy:
  - absolute HTTP/HTTPS only
  - block localhost + private/loopback literal IPs
- Redirects re-validated hop-by-hop.
- Response size bounded via truncation.
- Use a dedicated named client registration (for example `http_request`) with auto-redirect disabled so redirect hops are always validated explicitly.

---

### 2) `json_transform`
**Category:** `data`  
**Description:** Apply deterministic transforms to JSON payloads without arbitrary code execution.

**Parameters**
- `input` (required string/object/array)
- `operations` (required array of operations)
  - `select` (`path`)
  - `project` (`fields` array of `{name,path}`)
  - `filter_equals` (`path`, `value`) for array inputs
  - `sort` (`path`, `direction`)
  - `slice` (`offset`, `limit`)
  - `flatten` (`path`)

**Output**
- JSON string containing transformed result.

**Safety**
- No expression language or eval.
- Path grammar:
  - rootless paths only: `foo`, `foo.bar`, `items[0]`, `items[0].name`
  - no JSONPath operators (`$`, `..`, filters, wildcards)
  - array indices are zero-based integers in brackets
- Missing-path behavior:
  - `select`: returns `null`
  - `project`: field value becomes `null`
  - `filter_equals`: missing path does not match
  - `sort`: missing path sorts last
- Operation semantics:
  - `project.fields[*].name` is output property name; `project.fields[*].path` is source path
  - `flatten.path` targets an array value and flattens one level only when the targeted value is an array of arrays
- Limits on operation count and output size.

---

### 3) `csv_xlsx_read_write`
**Category:** `data`  
**Description:** Read/write CSV and XLSX within allowed filesystem root.

**Parameters**
- `operation` (required: `read` or `write`)
- `path` (required relative path within allowed root)
- `format` (optional; infer from extension if omitted)
- **Read options**:
  - `sheet` (xlsx; optional)
  - `has_header` (default true)
  - `max_rows` (default 200)
- **Write options**:
  - `rows` (required for write: array of objects or array of arrays)
  - `sheet` (xlsx write; default `Sheet1`)
  - `append` (csv only; default false)

**Output**
- `read`: JSON payload with `columns`, `rows`, `rowCount`, `truncated`
- `write`: summary message with rows written and target

**Safety**
- Reuse `FileSystemSupport.ResolveWithinRoot`.
- Enforce row/column/byte limits.
- Reject unsupported extensions/content.

**Dependencies**
- Add packages for robust parsing/writing:
  - `CsvHelper`
  - `ClosedXML`

---

### 4) `database_query`
**Category:** `data`  
**Description:** Execute parameterized SQL against named configured connections with read-only enforcement by default.

**Parameters**
- `connection` (required: configured connection name)
- `query` (required SQL)
- `parameters` (optional object map)
- `max_rows` (optional int; clamped to config max)
- `timeout_seconds` (optional int; clamped)

**Output**
- JSON with:
  - `columns`
  - `rows`
  - `rowCount`
  - `truncated`
  - `executionMs`

**Safety**
- Connection must exist in config.
  - Primary read-only enforcement is the configured DB credential/role (`ReadOnly = true`) with least-privilege permissions.
  - Secondary statement guardrails:
    - Allow `SELECT`, `WITH ... SELECT`, `EXPLAIN`
  - Block `INSERT/UPDATE/DELETE/ALTER/DROP/TRUNCATE/CREATE/GRANT/COPY/DO/CALL/EXECUTE`
  - Reject CTE-DML forms (for example `WITH x AS (INSERT ...) SELECT ...`)
  - Parameterized command execution only.
  - Optional schema allowlist check for postgres.
  - Command timeout enforced.

  **Dependencies**
  - Add DB providers:
    - `Npgsql`
    - `Microsoft.Data.Sqlite`

## Parallel implementation plan
Implementation will run in four parallel tracks with shared integration checkpoints:

1. **Track A:** `http_request` + tests
2. **Track B:** `json_transform` + tests
3. **Track C:** `csv_xlsx_read_write` + tests + package refs
4. **Track D:** `database_query` + config model + tests

Shared checkpoints after track completion:
- integration owner merges tool registrations in `ToolsServiceCollectionExtensions` to avoid multi-track conflicts
- integration owner merges `.csproj` package additions from Track C and Track D
- update appsettings/config binding files
- run full validation sequence

Parallel branch strategy:
- Each track commits its tool + tests independently.
- Shared-file merges happen in deterministic order: Track C (`.csproj`) -> Track D (`.csproj` + config) -> integration owner (`ToolsServiceCollectionExtensions` + final appsettings merge).
- Integration checkpoint includes a quick registry check to ensure `data` category tools are discoverable by default policies.

## Test plan
- Add unit tests under `test/LeanKernel.Tests.Unit/Tools/`:
  - `HttpRequestToolTests.cs`
  - `JsonTransformToolTests.cs`
  - `CsvXlsxReadWriteToolTests.cs`
  - `DatabaseQueryToolTests.cs`
- Validate:
  - required-field errors
  - safe-blocking behavior (SSRF/SQL write attempts/path traversal)
  - successful transformation/read/write/query flows
  - truncation/limit behavior

## Validation sequence
1. `dotnet restore src/LeanKernel.sln`
2. `dotnet build src/LeanKernel.sln --no-restore -v minimal`
3. `dotnet test src/LeanKernel.sln --no-build -v minimal`
4. `scripts/quality/test-coverage.sh`
5. `docker compose build`
6. `scripts/quality/sonarqube-scan.sh`

## Risks and mitigations
- **Risk:** SQL injection / destructive queries  
  **Mitigation:** parameterized execution + read-only statement allowlist.
- **Risk:** SSRF and local network abuse  
  **Mitigation:** strict URL validation + redirect revalidation.
- **Risk:** large payload/memory pressure  
  **Mitigation:** hard caps on rows/chars/operations.
- **Risk:** XLSX dependency weight  
  **Mitigation:** isolated tool package references and bounded read/write paths.

## Acceptance criteria
- All four tools are registered and callable.
- Each tool has deterministic validation errors and success outputs.
- Safety controls are enforced for network, filesystem, and SQL.
- Unit tests added and passing for each tool.
- Full validation sequence executed; any remaining gate failures are documented with scope.

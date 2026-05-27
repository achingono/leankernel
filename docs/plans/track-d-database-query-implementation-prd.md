# Track D: Database Query Tool Implementation PRD

## Scope

Implement a safe, read-only `database_query` tool for LeanKernel, supporting Postgres and SQLite, with strict config and SQL enforcement.

### Files
- `src/LeanKernel.Abstractions/Configuration/DatabaseQueryConfig.cs` (new)
- `src/LeanKernel.Abstractions/Configuration/LeanKernelConfig.cs` (add `DatabaseQuery` property)
- `src/LeanKernel.Gateway/appsettings.json` and `src/LeanKernel.Gateway/appsettings.Development.json` (defaults)
- `src/LeanKernel.Tools/BuiltIn/Data/DatabaseQueryTool.cs` (new)
- `src/LeanKernel.Tools/LeanKernel.Tools.csproj` (`Npgsql` and `Microsoft.Data.Sqlite` package refs)
- `test/LeanKernel.Tests.Unit/Tools/DatabaseQueryToolTests.cs` (new)

## Design

### Configuration
- Add `DatabaseQueryConfig`:
  - `MaxRows` (default `200`)
  - `DefaultTimeoutSeconds` (default `30`)
  - `Connections` (named connection definitions)
- Add connection config entry:
  - `Name`
  - `Provider` (`postgres` or `sqlite`)
  - `ConnectionString`
  - `ReadOnly` (default `true`)
  - `AllowedSchemas` (optional, postgres only)

### Tool Contract
- Name: `database_query`
- Category: `data`
- Parameters:
  - `connection` (required)
  - `query` (required)
  - `parameters` (optional object map)
  - `max_rows` (optional int, clamped)
  - `timeout_seconds` (optional int, clamped)
- Output JSON (deterministic order):
  - `columns`
  - `rows`
  - `rowCount`
  - `truncated`
  - `executionMs`

### Safety Controls
- Connection must exist in config by name.
- Provider must be `postgres` or `sqlite`.
- Read-only secondary guardrails:
  - Allow: `SELECT`, `WITH ... SELECT`, `EXPLAIN`
  - Block: `INSERT`, `UPDATE`, `DELETE`, `ALTER`, `DROP`, `TRUNCATE`, `CREATE`, `GRANT`, `COPY`, `DO`, `CALL`, `EXECUTE`
  - Reject CTE-DML forms (`WITH x AS (INSERT ...) SELECT ...` etc.)
  - Reject multiple statements (allow at most one statement, optional trailing `;` only)
- Parameterized command execution only (never interpolate values).
- Optional postgres schema allowlist enforcement from config.
- Clamp rows and timeouts to config bounds.
- Errors must be explicit and not leak connection strings/secrets.

### Implementation Notes
- Normalize SQL and remove comments/quoted strings for safer token checks.
- Bind parameters provider-specifically (`NpgsqlParameter`, `SqliteParameter`) or via `DbCommand.CreateParameter` with safe names.
- Set `CommandTimeout`; use cancellation token.
- Read `maxRows + 1` to detect truncation.
- Serialize result using a fixed-order object.

## Test Plan
- Required-field validation errors.
- Unknown connection error.
- Unsupported provider error.
- Read-only blocklist checks (`UPDATE`, `COPY`, `DO`, `CALL`, `EXECUTE`).
- CTE-DML rejection.
- Multiple statement rejection.
- SQLite in-memory success path with parameters.
- Row truncation behavior.
- SQL parser robustness for comments and quoted string literals.
- Explicit error messages.

## Security Checklist
- No SQL interpolation.
- No sensitive data in errors.
- Read-only guards robust against comment/string bypass patterns.
- Schema allowlist enforced for postgres connections when configured.

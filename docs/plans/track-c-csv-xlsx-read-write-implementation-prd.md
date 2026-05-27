# Track C PRD: `csv_xlsx_read_write` implementation

## Scope
- Add tool: `src/LeanKernel.Tools/BuiltIn/Data/CsvXlsxReadWriteTool.cs`
- Add tests: `test/LeanKernel.Tests.Unit/Tools/CsvXlsxReadWriteToolTests.cs`
- Add package refs in `src/LeanKernel.Tools/LeanKernel.Tools.csproj`:
  - `CsvHelper`
  - `ClosedXML`

## Constraints
- Do not edit `ToolsServiceCollectionExtensions.cs`.
- Do not edit appsettings or `LeanKernelConfig`.
- Reuse `FileSystemSupport.ResolveWithinRoot` and current allowed-root behavior.
- Support:
  - `operation`: `read`/`write`
  - format inference (`csv`/`xlsx`) from extension when omitted
  - bounded rows and deterministic output
  - write `rows` as array of objects or array of arrays
  - explicit, deterministic error handling

## Reviewed implementation plan
1. Add `CsvHelper` and `ClosedXML` package references to `LeanKernel.Tools.csproj`.
2. Implement static built-in tool `CsvXlsxReadWriteTool.Create(IServiceScopeFactory)`:
   - name: `csv_xlsx_read_write`
   - category: `data`
   - parameters: `operation`, `path`, `format`, `sheet`, `has_header`, `max_rows`, `rows`, `append`
3. Validate required args and operation enum (`read`/`write`) with explicit error messages.
4. Resolve path using:
   - configured `LeanKernelConfig.FileSystem.AllowedRoot`
   - `FileSystemSupport.ResolveWithinRoot(allowedRoot, path)`
   - reject unresolved/outside-root paths with explicit errors.
5. Infer format from `format` argument or file extension; allow only `csv` and `xlsx`.
6. Read flow:
   - validate file exists
   - parse `has_header` (default `true`)
   - parse `max_rows` (default `200`) and clamp to tool max
   - CSV read with `CsvHelper`
   - XLSX read with `ClosedXML` using requested sheet or first worksheet
   - deterministic response JSON:
     - `columns` (stable order)
     - `rows` (array-of-arrays in column order)
     - `rowCount`
     - `truncated`
7. Write flow:
   - require `rows`
   - support homogeneous input: array-of-objects or array-of-arrays (mixed types rejected)
   - object rows: column set = key union sorted with `StringComparer.Ordinal`
   - array rows: columns as `c1..cn` to max width
   - deterministic null/missing handling (empty cell serialization)
   - CSV:
     - supports append/overwrite
     - append allowed only for CSV
   - XLSX:
     - append rejected with explicit error
     - overwrite by recreating workbook/sheet (`sheet` default `Sheet1`)
   - deterministic write summary output.
8. Exception handling:
   - `UnauthorizedAccessException`, `IOException`, parse/format exceptions mapped to explicit tool errors
   - unsupported format, missing sheet, invalid rows schema handled explicitly.
9. Unit tests:
   - required args/operation errors
   - path safety via allowed root and traversal rejection
   - format inference and invalid format errors
   - bounded read behavior (`max_rows`, `truncated`)
   - CSV/XLSX read success
   - write success for object rows and array rows
   - append behavior (`csv` only) and `xlsx` append rejection
   - deterministic column ordering for object writes.

## Plan review record
- Reviewed by a different model (`claude-sonnet-4.5`) before implementation.
- Incorporated review outcomes:
  - explicit deterministic ordering (`StringComparer.Ordinal`)
  - explicit append semantics by format
  - explicit missing-sheet and unsupported-format errors
  - stronger max-row bounds and truncation behavior.

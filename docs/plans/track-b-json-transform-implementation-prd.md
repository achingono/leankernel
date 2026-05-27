# Track B: json_transform Implementation Plan

**Status:** Ready for Implementation  
**Tracker:** Tools 1-4 maximally-useful built-ins (Phase 1)  
**Scope:** JsonTransformTool.cs, JsonTransformToolTests.cs, ToolArgumentReader extensions only  

---

## Executive Summary

Implement a deterministic JSON transformation tool with bounded operations, rootless path grammar, and explicit missing-path semantics. No expression evaluation, no arbitrary code execution. Tool processes JSON payloads through a series of safe, verifiable operations (select, project, filter_equals, sort, slice, flatten).

---

## Requirements from PRD (tools-1to4-max-usefulness-prd.md)

### Parameters
- `input` (required: string/object/array) — JSON payload or native object/array from agent
- `operations` (required: array of operation objects)
  - Each operation has `op` (or `type`) field identifying operation type
  - Operation-specific required/optional fields (e.g., `path`, `fields`, `value`, `direction`)

### Supported Operations
1. **select** (`path`) — Select single value at path; return null if missing
2. **project** (`fields: [{name, path}]`) — Create object with named fields from source paths
3. **filter_equals** (`path`, `value`) — Filter array elements where path matches value; missing path does not match
4. **sort** (`path`, `direction`) — Sort array by path value; missing paths sort last
5. **slice** (`offset`, `limit`) — Slice array; ignore out-of-bounds gracefully
6. **flatten** (`path`) — Flatten one level only when targeted value is array of arrays

### Path Grammar (Strict, Rootless)
- **Valid:** `foo`, `foo.bar`, `items[0]`, `items[0].name`, `nested.array[0].property`
- **Invalid:** `$`, `..`, `foo[*]`, `foo.*`, wildcards, JSONPath expressions, filters
- Zero-based integer indices only; no negative indices (not in grammar spec)
- No whitespace around brackets or dots

### Missing-Path Semantics
| Operation | Behavior |
|-----------|----------|
| select | Returns `null` |
| project | Field value becomes `null` |
| filter_equals | Condition fails (element filtered out) |
| sort | Sorted to end of array |
| slice | No-op (path not evaluated) |
| flatten | No-op if path missing (array unchanged) |

### Limits & Validation
- **Max operations:** 50 per request (validation error if exceeded)
- **Max output size:** 500,000 characters (will truncate if needed; flag `truncated` in output)
- **Max array size for operations:** 10,000 elements (validation error if exceeded)
- Error messages must be actionable and specific (e.g., "Operation count exceeded: 52 > 50 max")

### Output Format
- JSON string representation of final result
- If result is scalar (from select), output as JSON scalar (not wrapped)
- If result is object/array, output as JSON object/array
- Preserve field order from projection operations

### Safety Constraints
- ✗ No expression language, no eval, no code execution
- ✗ No network access, no filesystem access
- ✓ Deterministic, repeatable (same input always produces same output)
- ✓ Bounded (operation count, output size, array size)

---

## Implementation Design

### 1. ToolArgumentReader Extensions

**File:** `src/LeanKernel.Tools/BuiltIn/Common/ToolArgumentReader.cs`

Add three new static methods to handle complex argument types:

```csharp
/// <summary>
/// Extracts and converts an argument to JsonNode if possible.
/// Handles string JSON, JsonElement (native), object, or array inputs.
/// </summary>
public static JsonNode? GetJsonNodeOrNull(IDictionary<string, object?> arguments, string name)
```

```csharp
/// <summary>
/// Extracts an argument as a Dictionary<string, object?>.
/// If argument is JsonElement of type Object, converts to dictionary.
/// If argument is already Dictionary, returns it.
/// Returns null if argument is missing or null.
/// </summary>
public static Dictionary<string, object?>? GetStringDictionary(IDictionary<string, object?> arguments, string name)
```

```csharp
/// <summary>
/// Extracts an argument as a JsonArray (or null).
/// Handles JsonElement of type Array or JsonArray directly.
/// Returns null if argument is missing, null, or not array-like.
/// </summary>
public static JsonArray? GetJsonArrayOrNull(IDictionary<string, object?> arguments, string name)
```

**Design Rationale:**
- `GetJsonNodeOrNull` handles the `input` parameter (string → parse JSON, native object → convert to JsonNode)
- `GetJsonArrayOrNull` handles the `operations` array parsing
- All methods handle both JsonElement (from tool protocol) and native types (from internal calls)
- Graceful null handling; no exceptions for missing optional arguments

---

### 2. JsonTransformTool Implementation

**File:** `src/LeanKernel.Tools/BuiltIn/Data/JsonTransformTool.cs`

Structure:
```csharp
public static class JsonTransformTool
{
    private const int MaxOperations = 50;
    private const int MaxOutputChars = 500_000;
    private const int MaxArrayElements = 10_000;

    public static ToolDefinition Create(IServiceScopeFactory scopeFactory)
    {
        return new ToolDefinition
        {
            Name = "json_transform",
            Description = "Apply deterministic transforms to JSON payloads: select, project, filter_equals, sort, slice, flatten",
            Category = "data",
            Parameters = new[] { 
                new ToolParameter { Name = "input", Type = "object", Required = true, Description = "JSON object, array, or JSON string to transform" },
                new ToolParameter { Name = "operations", Type = "array", Required = true, Description = "Array of operation objects..." }
            },
            Handler = HandleJsonTransform
        };
    }

    private static async Task<ToolResult> HandleJsonTransform(
        IDictionary<string, object?> arguments, 
        CancellationToken ct)
    {
        // 1. Parse input (string or native object/array)
        // 2. Validate operations array (count, structure)
        // 3. Apply each operation sequentially
        // 4. Enforce output size limit
        // 5. Return JSON string result or error
    }
}
```

#### Core Responsibilities:

**Input Parsing:**
- Accept string (parse as JSON), JsonElement, or native object/array via `ToolArgumentReader.GetJsonNodeOrNull()`
- Validate input is not null; return error if missing
- Return error if string is not valid JSON

**Operations Parsing & Validation:**
- Extract `operations` via `ToolArgumentReader.GetJsonArrayOrNull()`
- Validate array has 1–50 operations (inclusive)
- Validate array has ≤ 10,000 elements if input is array
- For each operation, validate `op`/`type` field and required fields per operation type
- Return early with distinct error if validation fails (e.g., "Operation 3: missing required field 'path'")

**Operation Dispatch:**
- Iterate through operations
- For each, call operation handler based on `op` field
- Pass result to next operation (pipeline style)
- Short-circuit on error

**Output & Truncation:**
- Serialize result to JSON string via `JsonSerializer.Serialize()`
- If length > MaxOutputChars, truncate and add `"_truncated": true` field (if result is object)
  - For array/scalar results truncated, include truncation notice in error message instead
- Return JSON string

---

### 3. Path Parser & Tokenizer

**File:** `src/LeanKernel.Tools/BuiltIn/Data/JsonTransformTool.cs` (nested class)

```csharp
internal class PathParser
{
    /// <summary>
    /// Parse a rootless path (e.g., "foo.bar[0].baz") into tokens.
    /// Returns null if path is invalid (fails grammar check).
    /// Tokens are: PropertyToken("name"), IndexToken(0), PropertyToken("nested"), etc.
    /// </summary>
    public static List<PathToken>? Parse(string path)
    {
        // Validate grammar:
        // - no $ . .. [ or ] outside brackets
        // - properties are alphanumeric_-
        // - indices are [0-9]+
        // - no whitespace around brackets/dots (unless in JSON strings, which don't apply)
        
        // Return list of tokens or null for invalid grammar
    }

    /// <summary>
    /// Evaluate path tokens against a JsonNode.
    /// Returns JsonNode at that path, or null if path missing.
    /// </summary>
    public static JsonNode? EvaluatePath(JsonNode root, List<PathToken> tokens)
    {
        // Navigate root → first token → ... → last token
        // If any step fails (property missing, index out of bounds), return null
    }

    internal abstract class PathToken { }
    internal class PropertyToken : PathToken { public string Name { get; set; } }
    internal class IndexToken : PathToken { public int Index { get; set; } }
}
```

**Design Rationale:**
- Strict grammar enforcement prevents injection/eval risks
- Token-based evaluation is clearer than regex-based navigation
- Separate parse and evaluate phases for testability
- Return null for missing paths consistently

---

### 4. Operation Handlers

**File:** `src/LeanKernel.Tools/BuiltIn/Data/JsonTransformTool.cs` (private static methods)

```csharp
private static JsonNode? ApplySelect(JsonNode input, JsonObject operation)
{
    // Get "path" from operation
    // Parse path; if invalid, throw OperationException with message
    // Evaluate against input; return JsonNode or null
    // Return null if path missing
}

private static JsonNode? ApplyProject(JsonNode input, JsonObject operation)
{
    // Get "fields" array from operation; validate each is {name, path}
    // For each field, evaluate path and store result
    // Return new JsonObject with field names and values (null if path missing)
    // Error if input is not object or array (for array, error or apply to each element?)
    //   → PRD unclear; choose: error if input is array (user must slice first)
}

private static JsonNode? ApplyFilterEquals(JsonNode input, JsonObject operation)
{
    // Require input is array; error if not
    // Get "path" and "value"
    // For each array element, evaluate path and check == value
    // Return filtered array
    // Missing paths do not match (are filtered out)
    // NULL values in path: NULL == NULL? Yes (use JsonValue.DeepEquals or similar)
}

private static JsonNode? ApplySort(JsonNode input, JsonObject operation)
{
    // Require input is array; error if not
    // Get "path" and "direction" (must be "asc" or "desc", case-insensitive)
    // Extract comparable values for each element at path
    // Sort with missing paths last, then by comparable value
    // Nulls: treat same as missing (sort to end)
    // Return sorted array
}

private static JsonNode? ApplySlice(JsonNode input, JsonObject operation)
{
    // Require input is array; error if not
    // Get "offset" (default 0) and "limit" (required, or default max-size)
    // Clamp offset/limit to array bounds (don't error on out-of-range)
    // Return sliced array
}

private static JsonNode? ApplyFlatten(JsonNode input, JsonObject operation)
{
    // Get "path"
    // Evaluate path; if result is not array, no-op (return input unchanged)
    // If result is array of arrays, flatten one level only
    // If result is array of non-arrays, no-op (return input unchanged)
    // Return modified input
}
```

**Shared Error Handling:**
- Throw `OperationException` with operation index and message for any operation-level error
- Catch and convert to `ToolResult.Error` in main handler
- Example: `"Operation 2 (filter_equals): expected input to be an array, got object"`

---

### 5. Deterministic Operation Semantics (Clarified from PRD)

| Op | Input Constraint | Missing Path Behavior | NULL Value Behavior | Output |
|----|--------------------|-------------------|--------------------|--------|
| select | any | return null | return null | JsonNode or null |
| project | object or skip arrays | field → null | field → null | JsonObject |
| filter_equals | array | element filtered out | null == null (false unless value is null) | JsonArray |
| sort | array | sort to end | same as missing | sorted JsonArray |
| slice | array | no-op | no-op | sliced JsonArray |
| flatten | any | no-op | no-op | input unchanged or flattened |

**Critical Clarifications for Implementation:**
1. **project on array:** Return error "project requires object input" (not per-element)
2. **filter_equals with NULL values:** Use `JsonValue.DeepEquals(path_val, filter_value)` for proper NULL equality
3. **flatten on non-array:**Return input unchanged (benign no-op)
4. **sort stability:** Use stable sort (e.g., `Array.Sort` with index tracking)

---

## Test Coverage Plan

**File:** `test/LeanKernel.Tests.Unit/Tools/JsonTransformToolTests.cs`

### Test Categories

#### Success Path Tests
- [ ] select on nested property returns correct value
- [ ] select on array index returns correct element
- [ ] project creates object with correct field names and values
- [ ] filter_equals filters array correctly
- [ ] sort orders array by path (asc/desc)
- [ ] slice returns correct subarray
- [ ] flatten flattens array of arrays one level
- [ ] chained operations (select → project → flatten)
- [ ] input as JSON string, object, array

#### Missing-Path Semantics Tests
- [ ] select on missing path returns null
- [ ] project field on missing path becomes null
- [ ] filter_equals on missing path filters element out
- [ ] sort on missing path moves element to end
- [ ] flatten on missing path is no-op
- [ ] slice ignores missing path (no-op)

#### Validation & Error Tests
- [ ] error if input is missing
- [ ] error if input is invalid JSON string
- [ ] error if operations array is missing
- [ ] error if operations array is empty
- [ ] error if operations count exceeds 50
- [ ] error if array elements exceed 10,000
- [ ] error if output truncated → includes `_truncated` flag
- [ ] error on invalid path grammar (e.g., `$foo`, `foo..bar`, `foo[abc]`)
- [ ] error on invalid operation type
- [ ] error on missing required operation field
- [ ] error if project input is array
- [ ] error if filter_equals input is not array
- [ ] error if sort input is not array
- [ ] error if slice input is not array

#### Edge Cases
- [ ] empty array input
- [ ] null value in array (not same as missing)
- [ ] deeply nested paths (5+ levels)
- [ ] projection with duplicate field names (last wins)
- [ ] sort with mixed types at path (numbers, strings, nulls)
- [ ] slice with offset > array length
- [ ] slice with limit = 0
- [ ] flatten on array of objects (no-op, not array of arrays)
- [ ] flatten on array of strings (no-op, not array of arrays)
- [ ] operations on JSON primitives (string, number, boolean)

#### Data Type Handling
- [ ] input as string (parse JSON)
- [ ] input as JsonElement (from tool protocol)
- [ ] input as native object/array (conversion)
- [ ] operations as JsonElement array
- [ ] value in filter_equals as string, number, boolean, null

---

## Constraints & Assumptions

### Design Decisions (for clarity)
1. **No recursive flattening:** flatten only goes one level deep, as per PRD
2. **Path indices are always 0-based:** No negative indices (e.g., `[-1]` is invalid)
3. **Project always returns object, never modifies input array:** If input is array, error (clarification of PRD ambiguity)
4. **Sort stability:** Use stable sort so order of equal-valued elements is preserved
5. **Output truncation:** If result > 500KB, truncate and mark `truncated` (for objects) or error message (for arrays/scalars)

### Out of Scope (deferred to integration)
- Registration in `ToolsServiceCollectionExtensions` (deferred; this plan only implements the tool)
- Dynamic tool registration from config
- Tool discovery/registry updates (handled separately)

### Testing Environment
- Use xUnit with Fluent Assertions
- Mock `IServiceScopeFactory` for tests (can pass null if not needed)
- Test with System.Text.Json (production serializer)

---

## File Checklist

- [ ] `src/LeanKernel.Tools/BuiltIn/Common/ToolArgumentReader.cs` — Add 3 new methods
- [ ] `src/LeanKernel.Tools/BuiltIn/Data/JsonTransformTool.cs` — Implement tool + path parser + operation handlers
- [ ] `test/LeanKernel.Tests.Unit/Tools/JsonTransformToolTests.cs` — Add unit tests
- [ ] (NO CHANGE) `ToolsServiceCollectionExtensions.cs` — registration deferred to integration track

---

## Quality Gates & Validation

1. **Unit Tests:** All JsonTransformToolTests pass
2. **Code Coverage:** ≥ 90% coverage for JsonTransformTool.cs
3. **Path Grammar:** Fuzzing with intentionally malformed paths (must reject safely)
4. **Large Payload:** Test with 5MB JSON input → verify truncation or rejection
5. **Operation Chaining:** Test 50-operation pipeline → verify performance and correctness

Run validation commands (from repo root):
```bash
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln --no-restore -v minimal
dotnet test src/LeanKernel.sln --no-build -v minimal --filter "JsonTransformTool"
scripts/quality/test-coverage.sh
```

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Path parser bugs (injection risk) | Security | Strict token-based parsing; reject invalid grammar early; fuzzing tests |
| Operation semantics ambiguity (flatten, project on arrays) | Correctness | Clarify in this plan; add explicit tests |
| Output truncation breaking downstream | UX/correctness | Flag truncation in output; document expected behavior |
| Large array operations (10K elements) | Performance | Limit array size; validate early; use efficient algorithms (e.g., stable sort) |
| ToolArgumentReader type coercion | Type safety | Handle JsonElement, native types, strings uniformly; explicit conversion tests |

---

## Success Criteria

✓ JsonTransformTool is implemented and callable  
✓ All six operations (select, project, filter_equals, sort, slice, flatten) work correctly  
✓ Path grammar is enforced strictly (no eval risk)  
✓ Missing-path semantics match PRD table  
✓ Output is bounded (size + operation count)  
✓ Unit tests pass (≥90% coverage)  
✓ Validation sequence runs cleanly (build + test + coverage)  

---

## Next Steps (After Approval)

1. Implement `ToolArgumentReader` extensions
2. Implement `JsonTransformTool` class and static methods
3. Implement `PathParser` and operation handlers
4. Write comprehensive unit tests
5. Run validation sequence
6. Commit with message: "Track B: Implement json_transform tool with deterministic operations"
7. Hand off to integration owner for registration in `ToolsServiceCollectionExtensions`

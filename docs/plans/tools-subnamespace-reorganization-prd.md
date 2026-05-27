# Tools sub-namespace reorganization PRD

## Summary
Reorganize built-in tools from one flat `BuiltIn` folder/namespace into grouped sub-folders and sub-namespaces so the codebase is easier to navigate and extend.

## Goals
- Group built-in tool code by domain instead of one flat folder.
- Introduce matching sub-namespaces for each folder.
- Keep tool names, behavior, and registration order unchanged.
- Update tests and imports so the solution remains build-clean.

## Non-goals
- No behavior changes to tool handlers.
- No changes to tool contracts exposed to agents.
- No governance or policy redesign.

## Target structure
- `BuiltIn/Common`
  - `ToolArgumentReader.cs`
  - `FileSystemSupport.cs`
- `BuiltIn/FileSystem`
  - all file/directory tools and `ExtractTextTool`
- `BuiltIn/Internet`
  - `WebFetchTool.cs`, `WebSearchTool.cs`
- `BuiltIn/Knowledge`
  - `WikiSearchTool.cs`, `WikiReadTool.cs`, `WikiWriteTool.cs`

## Namespace mapping
- `LeanKernel.Tools.BuiltIn.Common`
- `LeanKernel.Tools.BuiltIn.FileSystem`
- `LeanKernel.Tools.BuiltIn.Internet`
- `LeanKernel.Tools.BuiltIn.Knowledge`

`ToolsServiceCollectionExtensions` remains in `LeanKernel.Tools` and imports the grouped namespaces.

## Cross-group dependencies
- `ToolArgumentReader` and `FileSystemSupport` live in `BuiltIn/Common`.
- Every tool class in `FileSystem`, `Internet`, and `Knowledge` imports `LeanKernel.Tools.BuiltIn.Common`.
- `WebFetchTool` (Internet) depends on `FileSystemSupport` and `TextExtractionHelper` (Common).

## Registration imports
Replace the current single import in `ToolsServiceCollectionExtensions.cs`:

```csharp
using LeanKernel.Tools.BuiltIn;
```

with:

```csharp
using LeanKernel.Tools.BuiltIn.Common;
using LeanKernel.Tools.BuiltIn.FileSystem;
using LeanKernel.Tools.BuiltIn.Internet;
using LeanKernel.Tools.BuiltIn.Knowledge;
```

## Implementation plan
1. Move `Common` helpers first:
   - `ToolArgumentReader.cs`
   - `FileSystemSupport.cs`
2. Move and update `FileSystem` tools.
3. Move and update `Internet` tools.
4. Move and update `Knowledge` tools.
5. Update each moved file namespace and add `using LeanKernel.Tools.BuiltIn.Common;` where needed.
6. Update tool registration imports in `ToolsServiceCollectionExtensions` to the four grouped imports above.
7. Update tests to import the new namespaces (mapping below).
8. Delete original flat files after confirming moved files compile.
9. Run restore/build/test and repository quality checks.

## Test import mapping
| Test file | Required imports |
| --- | --- |
| `WebFetchToolTests.cs` | `LeanKernel.Tools.BuiltIn.Internet` (+ `BuiltIn.Common` if helpers are referenced) |
| `FileSystemToolsTests.cs` | `LeanKernel.Tools.BuiltIn.FileSystem` |
| `FileSystemAdvancedToolTests.cs` | `LeanKernel.Tools.BuiltIn.FileSystem` |
| `WikiToolTests.cs` | `LeanKernel.Tools.BuiltIn.Knowledge` |

## Risks
- Namespace drift causing compile errors in tool registrations and tests.
- Cross-group helper references breaking during file moves.
- Duplicate type errors if original flat files are not removed after migration.

## Acceptance criteria
- Built-in tools are organized into the sub-folders above.
- Namespaces align with folder groups.
- Tool behavior and tool names are unchanged.
- Unit tests compile and pass with the new namespaces.

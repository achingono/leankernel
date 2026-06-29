# Runtime Skills

LeanKernel supports dynamic runtime skills loaded from `SKILL.md` files and exposed as tool operations.

## Implemented Behavior

- Gateway startup calls `AddLeanKernelSkills()` to initialize the runtime skill registry.
- Registry scans configured base paths for `SKILL.md` files recursively.
- Parsed skills are validated (required name/description/operations, runtime constraints).
- Valid skill operations are registered into the shared `IToolRegistry` as dynamic tools.
- Invalid or unreadable skill files are quarantined and logged.

## Configuration

- `LeanKernel:Skills:BasePaths` controls which directories are scanned.
- `LeanKernel:Skills:Enabled` exists in configuration models; current loader behavior always initializes and uses `BasePaths`.

## Related Pages

- [Tools index](tools/index.md)
- [Skills docs](../skills/index.md)
- [Configuration reference](../configuration/configuration-reference.md)

## Source References

- `src/LeanKernel.Plugins/BuiltIn/Skills/SkillExtensions.cs`
- `src/LeanKernel.Plugins/BuiltIn/Skills/RuntimeSkillRegistry.cs`
- `src/LeanKernel.Abstractions/Configuration/LeanKernelConfig.cs`

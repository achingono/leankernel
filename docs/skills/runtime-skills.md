# Runtime Skills

LeanKernel runtime skills are markdown-defined capabilities loaded from configured file-system paths.

## Lifecycle

1. Skill files are discovered under `LeanKernel:Skills:BasePaths`.
2. `SKILL.md` documents are parsed.
3. Skill definitions are registered into runtime tool registry.
4. Dynamic skill tools are exposed to agent orchestration.

## Related Pages

- [Skills index](index.md)
- [Skill format](skill-format.md)
- [Feature: tool governance](../features/tool-governance.md)

## Source References

- `src/LeanKernel.Tools`
- `src/LeanKernel.Agents`

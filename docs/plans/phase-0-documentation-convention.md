# Phase 0 — Documentation Convention PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers, implementers, and reviewers
- **Scope owner:** LeanKernel rearchitecture program
- **Phase goal:** Establish the documentation contract that every rearchitecture phase must follow before implementation work expands

## Executive summary

Phase 0 defines the documentation baseline for the LeanKernel rearchitecture. The goal is to make documentation deliverables explicit before feature implementation accelerates so that feature behavior, API surface, configuration changes, and roadmap material remain easy to find and implementation-accurate.

This PRD covers contributor guidance, placeholder indexes for feature and configuration documentation, and updates to the main documentation landing page. It is documentation-only work and does not change runtime behavior.

## Goals

1. Define the minimum documentation that each implementation phase must produce.
2. Establish consistent naming and structure rules for rearchitecture docs.
3. Create navigable entry points for feature and configuration documentation.
4. Update the documentation landing page to reflect the new structure.

## Non-goals

- Writing phase-specific feature documentation
- Defining actual Phase 1, 2, or 3 configuration values
- Changing application code, runtime behavior, or APIs
- Replacing existing architecture, skills, or development documentation

## Deliverables

### Documentation contribution guide

Create `docs/CONTRIBUTING-DOCS.md` to define:
- per-phase documentation requirements
- documentation standards
- file naming conventions
- a contributor checklist for documentation hygiene

### Feature documentation index

Update `docs/features/index.md` to act as a phase-based placeholder index for implemented feature docs.

### Configuration documentation index

Create `docs/configuration/index.md` as the landing page for per-phase configuration references. Placeholder links to future phase documents are intentional in this phase.

### Documentation landing page update

Update `docs/index.md` so contributors can discover:
- architecture docs
- feature docs
- configuration docs
- plan docs
- the documentation contribution guide

## Acceptance criteria

- `docs/CONTRIBUTING-DOCS.md` exists and defines the required per-phase documentation outputs.
- `docs/features/index.md` contains the requested phase placeholder structure.
- `docs/configuration/index.md` exists under a new `docs/configuration/` directory.
- `docs/index.md` links to the new documentation structure and contribution guide.
- The Phase 0 documentation todo is marked complete in the session database.

## Validation

Validation for this phase consists of:
- reviewing the resulting Markdown files for structure and relative links
- checking the repository diff to confirm the change is documentation-only
- skipping build and test commands because this task only changes documentation and no documentation-specific test tooling was requested

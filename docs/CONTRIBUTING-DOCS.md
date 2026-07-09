# Documentation Contribution Guide

This guide establishes the documentation convention for the LeanKernel rearchitecture project. Every implementation phase must ship documentation that is accurate, practical, and easy to navigate from the main [documentation index](index.md).

## Purpose

Use this guide when adding or updating documentation for rearchitecture work. It defines the minimum documentation set for each phase, the standards all docs must follow, and the naming rules for new files.

See also:
- [Architecture documentation](architecture/index.md)
- [Feature documentation](features/index.md)
- [Configuration reference](configuration/index.md)
- [Plans](plans/index.md)

## Per-Phase Documentation Requirements

Each rearchitecture phase must produce the following documentation.

### 1. Feature documentation

Create feature documentation in `docs/features/` with **one Markdown file per major feature**.

Each feature document must cover:
- Purpose and use case
- How it works (with diagrams where helpful)
- Configuration options
- API endpoints (if applicable)
- Examples

**File format:** `docs/features/{feature-name}.md`

**Example:** `docs/features/context-gating.md`

### 2. API documentation

API documentation must be generated from the implemented Minimal API surface.

Requirements:
- OpenAPI/Swagger must be auto-generated from Minimal API endpoints.
- The Gateway project must configure Swagger generation.
- Endpoint documentation should come from the implementation, not from a manually maintained duplicate list.

### 3. Configuration reference

Each phase must add a configuration reference file in `docs/configuration/` that lists all new configuration introduced by that phase.

Each configuration reference must include:
- Configuration key
- Type
- Default value
- Description

**File format:** `docs/configuration/phase-{n}-config.md`

**Example:** `docs/configuration/configuration-reference.md`

### 4. Updated README.md

`README.md` must always reflect the current implemented state of the system.

Update the README when a phase changes:
- Implemented capabilities
- Setup or runtime expectations
- Public API surface
- Configuration or deployment guidance

Do not describe unfinished work as complete. Mark forward-looking items as planned work.

## Documentation Standards

All LeanKernel documentation must follow these standards:

- Use Mermaid for diagrams so they render in GitHub.
- Keep docs implementation-accurate; label forward-looking work as **Planned** or **Roadmap**.
- Include practical examples, not just abstract descriptions.
- Link related documents using relative paths.
- Use a consistent heading structure: `# Title`, `## Section`, `### Subsection`.

## File Naming Conventions

Use these file naming patterns consistently:

- Feature docs: `docs/features/{feature-name}.md`
- Config docs: `docs/configuration/phase-{n}-config.md`
- Plan docs: `docs/plans/{plan-name}.md`
- Architecture docs: `docs/architecture/{topic}.md`

Use kebab-case file names for all new documentation files.

## Contribution Checklist

Before completing a phase or feature PR, confirm the following:

- [ ] Feature documentation exists for each major implemented feature.
- [ ] OpenAPI/Swagger generation covers new Minimal API endpoints.
- [ ] The phase configuration reference is updated with new keys.
- [ ] `README.md` reflects the current implemented state.
- [ ] New and updated docs link to related documentation using relative paths.
- [ ] Planned work is clearly labeled as planned.

Following this guide keeps the rearchitecture documentation consistent across phases and makes it easier to keep implementation, API surface, and configuration guidance in sync.

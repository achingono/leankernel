# Documentation Implementation Alignment PRD (2026-06-29)

## Overview

Align core contributor and operator documentation with the current implementation in `src/LeanKernel.sln` and the active `LeanKernel.Gateway` runtime surface.

## Goals

- Update required documentation pages to match implemented projects, endpoints, configuration keys, and UI pages.
- Add missing feature/API pages where behavior exists in code but is not documented.
- Keep legacy references safe by preserving redirects/stubs and avoiding speculative content.

## Non-Goals

- No runtime behavior changes.
- No removal of legacy docs unless clearly safe and requested.
- No broad docs rewrite outside requested files.

## Scope

1. Update `README.md` project map to reflect current `src` solution projects and current role pairings.
2. Update `docs/architecture/solution-structure.md` for missing projects and clear Gateway vs Host status.
3. Update configuration docs to include `LeanKernel:Skills` and currently-used gateway/forwarded-auth keys.
4. Update API docs to include `/healthz` and host API status note.
5. Update features index pages to match implemented Gateway UI routes and feature surfaces.
6. Update docs inventory matrix status rows for touched docs.
7. Add concise docs for runtime skills and Gateway middleware behavior.

## Source of Truth

- `src/LeanKernel.sln`
- `src/LeanKernel.Gateway/Program.cs`
- `src/LeanKernel.Gateway/Endpoints.cs`
- `src/LeanKernel.Abstractions/Configuration/LeanKernelConfig.cs`
- `src/LeanKernel.Plugins/BuiltIn/Skills/*.cs`
- `src/LeanKernel.Gateway/Components/Pages/*.razor`
- `src/LeanKernel.Gateway/Middleware/*.cs`

## Execution Plan

1. Confirm project inventory and responsibilities from solution + registrations.
2. Confirm endpoint/auth/health behavior from Gateway code.
3. Confirm config key names from options classes and direct configuration reads.
4. Edit required docs and add missing concise pages.
5. Update docs inventory matrix statuses for changed pages.
6. Run a final consistency pass for links and wording.

## Validation

- Every newly documented key/endpoint/page must map to code references listed above.
- No speculative claims about disabled, planned, or scaffold-only projects.
- Cross-links resolve within docs structure.

## Plan Review Note

- Plan reviewed against existing docs style and source-code references before edits.

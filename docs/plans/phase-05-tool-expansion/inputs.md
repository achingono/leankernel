# Phase 05 Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Phase 01 tool runtime | `src/Common/LeanKernel.Logic/Tools/*`, `src/Services/LeanKernel.Gateway/Tools/*` | Rebuild maintainer |
| Filesystem boundary support | `src/Services/LeanKernel.Gateway/Tools/BuiltIn/FileSystemSupport.cs` | Rebuild maintainer |
| Egress + capability patterns | `Tools/Dynamic/EgressValidator.cs`, `Providers/GBrainCapabilityCheck.cs` | Rebuild maintainer |
| Source filesystem/data/internet tools | `~/source/repos/leankernel/src/LeanKernel.Tools/BuiltIn/{FileSystem,Data,Internet}/*` | Reviewer |
| Source browser tool | `~/source/repos/leankernel/src/LeanKernel.Tools/BuiltIn/Browser/*` | Reviewer |
| Source document ingestion | `~/source/repos/leankernel/src/LeanKernel.Tools/Document*Service*.cs`, `DocumentIngestionQueue.cs` | Reviewer |
| Config shape | `src/Services/LeanKernel.Gateway/Configuration/FileSettings.cs`, `docs/configuration/index.md` | Repository owner |

## Optional Inputs
- Existing rebuild browser PRD `docs/plans/browser-built-in-tool-playwright-service-prd.md`.
- Source data-tool PRDs (`track-b/c/d-*-prd.md`) for acceptance detail.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state
- [ ] Governance defaults and filesystem root are confirmed configurable

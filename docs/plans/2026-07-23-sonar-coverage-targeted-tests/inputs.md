# Sonar Coverage Inputs

## Required Inputs

| Input | Source | Owner |
|---|---|---|
| Baseline quality gate status (`new_coverage` 73%) | SonarQube API (`/api/qualitygates/project_status`) | Coding agent |
| New-code hotspots by uncovered lines | SonarQube API (`/api/measures/component_tree`) | Coding agent |
| Current scan workflow and flags | `scripts/quality/sonarqube-scan.sh` | Repository |
| Target production files under test | `src/Services/LeanKernel.Gateway/...`, `src/Common/LeanKernel.Logic/...` | Repository |
| Existing test patterns and helpers | `test/LeanKernel.Tests.Unit`, `test/LeanKernel.Tests.Integration` | Repository |

## Baseline Hotspots (New Uncovered Lines)

| File | New Uncovered Lines | New Coverage |
|---|---:|---:|
| `src/Services/LeanKernel.Gateway/Memory/GBrainDocumentStoreClient.cs` | 90 | 4.4% |
| `src/Services/LeanKernel.Gateway/Providers/AttachmentIngestionMiddleware.cs` | 82 | 10.1% |
| `src/Common/LeanKernel.Logic/Tools/DocumentIngestion/WatchFolderHostedService.cs` | 80 | 11.0% |
| `src/Services/LeanKernel.Gateway/Requests/DocumentUploadEndpoint.cs` | 63 | 4.1% |

## Optional Inputs
- Prior Sonar scan logs for comparison of per-file improvements.

## Input Validation Checklist
- [ ] All required inputs are current (not from a superseded version)
- [ ] No required input is missing or in draft state

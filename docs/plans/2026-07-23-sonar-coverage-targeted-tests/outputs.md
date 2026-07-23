# Sonar Coverage Outputs

## Mandatory Outputs

| Output | Description | Format |
| --- | --- | --- |
| Targeted test additions | New/updated unit or integration test files covering selected hotspots | C# source |
| Verification logs | Test run and Sonar scan results showing gate status | Command output |
| Coverage outcome summary | Before/after `new_coverage` and impacted files | Markdown notes |

## Optional Outputs
- Follow-up backlog note for remaining low-coverage non-gating files.

## Output Quality Checklist
- [x] All mandatory outputs produced
- [x] All outputs reviewed before gate
- [x] Evidence log updated with output references

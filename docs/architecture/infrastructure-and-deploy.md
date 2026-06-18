# Infrastructure and Deploy

Local and CI workflows use Gateway plus dependency services (for example Postgres, LiteLLM, and GBrain).

## Runtime Notes

- Gateway can run standalone for UI-focused local work.
- Missing sidecars may produce probe warnings in logs.
- CI build/test expectations are defined in workflow YAML.

## Related Pages

- [Getting started](../getting-started/index.md)
- [Development build and test](../development/build-and-test.md)
- [Operations](../operations/index.md)

## Source References

- `.github/workflows/build-and-test.yml`
- `docker-compose.yml`
- `src/LeanKernel.Gateway/Program.cs`

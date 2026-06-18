# Troubleshooting Startup

## Common Startup Issues

- `ERR_CONNECTION_REFUSED`: Gateway is not listening yet; confirm `dotnet run` succeeded and URL/port match tests.
- DI failure for scoped/singleton mismatch: review service lifetimes in Gateway composition and recent changes.
- Dependency probe warnings: expected when running Gateway without all Docker sidecars.

## Quick Checks

```bash
dotnet build src/LeanKernel.sln --no-restore -v minimal
dotnet run --project src/LeanKernel.Gateway/LeanKernel.Gateway.csproj --urls "http://127.0.0.1:5080"
curl -sSf "http://127.0.0.1:5080/api/health"
```

## Related Pages

- [Operations](../operations/index.md)
- [Health and observability](../operations/health-and-observability.md)
- [Configuration](../configuration/index.md)

## Source References

- `src/LeanKernel.Gateway/Program.cs`
- `src/LeanKernel.Gateway/Endpoints.cs`
- `src/LeanKernel.Gateway/LeanKernelHardeningServiceCollectionExtensions.cs`

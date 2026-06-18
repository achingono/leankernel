# Quick Start

Run LeanKernel locally with CI-aligned defaults.

## Prerequisites

- `.NET SDK 10.0.x`
- `Node.js 18`

## Build and Run

```bash
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln --no-restore -v minimal
dotnet run --project src/LeanKernel.Gateway/LeanKernel.Gateway.csproj --urls "http://127.0.0.1:5080"
```

Open `http://127.0.0.1:5080`.

## Related Pages

- [Local development](local-development.md)
- [Local testing](local-testing.md)
- [Troubleshooting startup](troubleshooting-startup.md)

## Source References

- `src/LeanKernel.Gateway/LeanKernel.Gateway.csproj`
- `.github/workflows/build-and-test.yml`

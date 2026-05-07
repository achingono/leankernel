# LeanKernel quality gates

LeanKernel quality validation is intentionally runnable from a clean developer workstation using the existing .NET SDK and Docker.

## Standard validation

```bash
dotnet restore src/LeanKernel.sln
dotnet build src/LeanKernel.sln -c Release --no-restore
dotnet test src/LeanKernel.sln -c Release --no-build
```

## Coverage gate

Run tests with coverage and enforce the default 80 percent line coverage gate:

```bash
scripts/quality/test-coverage.sh
```

Override the threshold when needed:

```bash
COVERAGE_THRESHOLD=85 scripts/quality/test-coverage.sh
```

The script writes Cobertura reports under `coverage-results/` and uses `scripts/quality/coverage-gate.py` to aggregate line coverage by source file and line number.

Coverage excludes generated files, EF migrations, Razor components, startup/auth registration glue, external channel/process adapters, hosted-service loops, and runtime skill execution boundaries. Those paths are integration surfaces that need environment-backed tests rather than line coverage in the unit gate.

## Local SonarQube scan

Run a local SonarQube Community Edition server in Docker and execute the .NET scanner from a .NET SDK container:

```bash
scripts/quality/sonarqube-scan.sh
```

Useful environment variables:

| Variable | Default | Purpose |
| --- | --- | --- |
| `SONAR_HOST_URL` | `http://localhost:9000` | URL used by the host script to check SonarQube status. |
| `SONAR_SCANNER_HOST_URL` | `http://host.docker.internal:9000` on macOS, otherwise `http://localhost:9000` | URL used from inside the scanner container. |
| `SONAR_PROJECT_KEY` | `LeanKernel` | SonarQube project key. |
| `SONAR_TOKEN` | empty | Existing token to use. If omitted, the script tries to generate a local token with `SONAR_LOGIN`/`SONAR_PASSWORD`. |
| `SONAR_LOGIN` | `admin` | Local SonarQube username used only when generating a token. |
| `SONAR_PASSWORD` | `admin` | Local SonarQube password used only when generating a token. |

The scan uses OpenCover output from Coverlet and waits for the SonarQube quality gate.

## Docker image validation

Build the runtime image after quality gates pass:

```bash
docker build -t LeanKernel-engine:local .
```

If an image scanner such as Trivy is installed, scan the image before publishing:

```bash
trivy image LeanKernel-engine:local
```

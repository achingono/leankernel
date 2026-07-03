#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/docker-compose.sonar.yml"
SONAR_HOST_URL="${SONAR_HOST_URL:-http://localhost:9000}"
SONAR_PROJECT_KEY="${SONAR_PROJECT_KEY:-LeanKernel}"
SONAR_LOGIN="${SONAR_LOGIN:-admin}"
SONAR_PASSWORD="${SONAR_PASSWORD:-admin}"

if [[ -z "${SONAR_SCANNER_HOST_URL:-}" ]]; then
  SONAR_SCANNER_HOST_URL="http://host.docker.internal:9000"
fi

docker compose -f "$COMPOSE_FILE" up -d sonarqube

echo "Waiting for SonarQube at $SONAR_HOST_URL ..."
for _ in $(seq 1 80); do
  status="$(curl -fsS "$SONAR_HOST_URL/api/system/status" 2>/dev/null | python3 -c 'import json,sys; print(json.load(sys.stdin).get("status", ""))' 2>/dev/null || true)"
  if [[ "$status" == "UP" ]]; then
    break
  fi
  sleep 5
done

if [[ "${status:-}" != "UP" ]]; then
  echo "SonarQube did not become ready. Last status: ${status:-unavailable}" >&2
  exit 1
fi

token="${SONAR_TOKEN:-}"
if [[ -z "$token" ]]; then
  token_name="LeanKernel-local-scan-$(date +%s)"
  token_response="$(curl -fsS -u "$SONAR_LOGIN:$SONAR_PASSWORD" \
    -X POST "$SONAR_HOST_URL/api/user_tokens/generate" \
    --data-urlencode "name=$token_name")"
  token="$(python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])' <<<"$token_response")"
fi

# If running in GitHub Actions, expose the token to subsequent steps.
# The workflow references steps.sonar-scan.outputs.sonar_token.
if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
  echo "sonar_token=$token" >> "$GITHUB_OUTPUT"
fi

rm -rf "$ROOT_DIR/coverage-results/sonar"
mkdir -p "$ROOT_DIR/coverage-results/sonar"

docker run --rm \
  --add-host=host.docker.internal:host-gateway \
  -e "SONAR_PROJECT_KEY=$SONAR_PROJECT_KEY" \
  -e "SONAR_HOST_URL=$SONAR_SCANNER_HOST_URL" \
  -e "SONAR_TOKEN=$token" \
  -v "$ROOT_DIR:/workspace" \
  -v "LeanKernel-nuget-cache:/root/.nuget/packages" \
  -w /workspace \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  bash -lc '
    set -euo pipefail
    apt-get update
    apt-get install -y --no-install-recommends nodejs openjdk-17-jre-headless python3
    rm -rf /var/lib/apt/lists/*
    dotnet tool install --global dotnet-sonarscanner
    export PATH="$PATH:/root/.dotnet/tools"
    dotnet sonarscanner begin \
      /k:"$SONAR_PROJECT_KEY" \
      /d:sonar.host.url="$SONAR_HOST_URL" \
      /d:sonar.token="$SONAR_TOKEN" \
      /d:sonar.scm.disabled=true \
      /d:sonar.qualitygate.wait=true \
      /d:sonar.python.version=3.12 \
      /d:sonar.cs.opencover.reportsPaths="coverage-results/sonar/coverage.opencover.xml" \
      /d:sonar.cpd.exclusions="test/**,config/webwright/**/*.py,src/LeanKernel.Tools/BuiltIn/Browser/BrowserToolDefinitions.cs,src/LeanKernel.Tools/BuiltIn/Data/*.cs,src/LeanKernel.Tools/BuiltIn/FileSystem/*.cs,src/LeanKernel.Host/Services/SelfConfigurationStep.cs,src/LeanKernel.Host/Services/UserConfigurationStep.cs,src/LeanKernel.Host/Templates/*.template" \
      /d:sonar.coverage.exclusions="scripts/**/*.py,config/litellm/*.py,config/indexer/**/*.py,config/webwright/**/*.py,**/LeanKernel.Tests.*/*,**/obj/**/*.cs,**/*.g.cs,**/*.Designer.cs,**/*.razor,**/Program.cs,**/Migrations/*.cs,**/Data/Migrations/*.cs,**/LeanKernel.Abstractions/Configuration/WebwrightConfig.cs,**/LeanKernel.Abstractions/Models/WebwrightModels.cs,**/LeanKernel.Gateway/Endpoints.cs,**/LeanKernel.Gateway/LeanKernelHardeningServiceCollectionExtensions.cs,**/LeanKernel.Gateway/Middleware/CorrelationIdDelegatingHandler.cs,**/LeanKernel.Gateway/Middleware/CorrelationIdMiddleware.cs,**/LeanKernel.Gateway/Models/ChatRequest.cs,**/LeanKernel.Gateway/Services/ChatService.cs,**/LeanKernel.Gateway/Services/DiagnosticsService.cs,**/LeanKernel.Gateway/Services/KnowledgeUiService.cs,**/LeanKernel.Gateway/Services/OnboardingService.cs,**/LeanKernel.Knowledge/GBrainKnowledgeService.cs,**/LeanKernel.Knowledge/Resilience/ResilientKnowledgeService.cs,**/LeanKernel.Persistence/DocumentIngestionJobRepository.cs,**/LeanKernel.Tools/DocumentFolderIngestionHostedService.cs,**/LeanKernel.Tools/DocumentIngestionHostedService.cs,**/LeanKernel.Tools/BuiltIn/Browser/WebwrightClient.cs,**/LeanKernel.Tools/BuiltIn/Browser/WebwrightHealthProbe.cs,**/LeanKernel.Tools/BuiltIn/Common/FileSystemSupport.cs,**/LeanKernel.Tools/BuiltIn/Common/ToolArgumentReader.cs,**/LeanKernel.Tools/BuiltIn/Data/*.cs,**/LeanKernel.Tools/BuiltIn/FileSystem/FileCopyTool.cs,**/LeanKernel.Tools/BuiltIn/FileSystem/FileDeleteTool.cs,**/LeanKernel.Tools/BuiltIn/FileSystem/FileMoveTool.cs,**/LeanKernel.Tools/BuiltIn/Internet/HttpRequestTool.cs,**/LeanKernel.Tools/BuiltIn/Internet/WebFetchTool.cs,**/LeanKernel.Tools/BuiltIn/Internet/WebSearchTool.cs,**/Services/Auth/AuthRegistration.cs,**/Services/Auth/OidcRegistration.cs,**/Services/Auth/BearerTokenAuthHandler.cs,**/Services/EngagementAuthorizationFilter.cs,**/Services/ChannelInitializationService.cs,**/Services/Skills/SkillHostedService.cs,**/Services/AttachmentTextExtractionService.cs,**/Services/EngagementRulesProvider.cs,**/LeanKernel.Commander/Adapters/SignalRestApiAdapter.cs,**/LeanKernel.Plugins/BuiltIn/Skills/DynamicSkillTool.cs,**/LeanKernel.Plugins/BuiltIn/Skills/BinaryResolver.cs,**/LeanKernel.Plugins/BuiltIn/Skills/DynamicSkillToolFactory.cs,**/LeanKernel.Plugins/BuiltIn/Skills/EgressPolicy.cs,**/LeanKernel.Plugins/BuiltIn/Skills/RuntimeSkillRegistry.cs,**/LeanKernel.Generators/ToolRegistryGenerator.cs"
    dotnet restore src/LeanKernel.sln
    dotnet build src/LeanKernel.sln -c Release --no-restore
    
    # Collect coverage from all test projects so Sonar can compute accurate coverage.
    dotnet test test/LeanKernel.Tests.Unit/LeanKernel.Tests.Unit.csproj -c Release --no-build \
      --collect:"XPlat Code Coverage" \
      --settings test/LeanKernel.Tests.Unit/coverage.sonar.runsettings \
      --results-directory coverage-results/sonar
    
    dotnet test test/LeanKernel.Tests.Integration/LeanKernel.Tests.Integration.csproj -c Release --no-build \
      --collect:"XPlat Code Coverage" \
      --settings test/LeanKernel.Tests.Unit/coverage.sonar.runsettings \
      --results-directory coverage-results/sonar

    python3 - <<'PY'
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET

results_dir = Path("coverage-results/sonar")
reports = sorted(results_dir.glob("**/coverage.opencover.xml"))
if not reports:
    raise SystemExit("No coverage reports were produced.")

def score(report: Path) -> tuple[int, int]:
    root = ET.parse(report).getroot()
    points = root.findall(".//SequencePoint")
    covered = sum(1 for point in points if int(point.attrib.get("vc", "0")) > 0)
    return covered, len(points)

best_report = max(reports, key=score)
shutil.copyfile(best_report, results_dir / "coverage.opencover.xml")
print(f"Selected coverage report: {best_report}")
PY

    #dotnet test test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj -c Release --no-build \
    #  --collect:"XPlat Code Coverage" \
    #  --settings test/LeanKernel.Tests.Unit/coverage.sonar.runsettings \
    #  --results-directory coverage-results/sonar
    dotnet sonarscanner end /d:sonar.token="$SONAR_TOKEN"
  '

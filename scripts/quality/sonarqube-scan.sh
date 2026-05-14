#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/docker-compose.sonar.yml"
SONAR_HOST_URL="${SONAR_HOST_URL:-http://localhost:9000}"
SONAR_PROJECT_KEY="${SONAR_PROJECT_KEY:-LeanKernel}"
SONAR_LOGIN="${SONAR_LOGIN:-admin}"
SONAR_PASSWORD="${SONAR_PASSWORD:-admin}"

if [[ -z "${SONAR_SCANNER_HOST_URL:-}" ]]; then
  if [[ "$(uname -s)" == "Darwin" ]]; then
    SONAR_SCANNER_HOST_URL="http://host.docker.internal:9000"
  else
    SONAR_SCANNER_HOST_URL="$SONAR_HOST_URL"
  fi
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

mkdir -p "$ROOT_DIR/coverage-results/sonar"

docker run --rm \
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
    apt-get install -y --no-install-recommends nodejs openjdk-17-jre-headless
    rm -rf /var/lib/apt/lists/*
    dotnet tool install --global dotnet-sonarscanner
    export PATH="$PATH:/root/.dotnet/tools"
    dotnet sonarscanner begin \
      /k:"$SONAR_PROJECT_KEY" \
      /d:sonar.host.url="$SONAR_HOST_URL" \
      /d:sonar.token="$SONAR_TOKEN" \
      /d:sonar.qualitygate.wait=true \
      /d:sonar.python.version=3.12 \
      /d:sonar.cs.opencover.reportsPaths="coverage-results/sonar/**/coverage.opencover.xml" \
      /d:sonar.cpd.exclusions="src/LeanKernel.Host/Services/SelfConfigurationStep.cs,src/LeanKernel.Host/Services/UserConfigurationStep.cs,src/LeanKernel.Host/Templates/*.template" \
      /d:sonar.coverage.exclusions="scripts/**/*.py,config/litellm/*.py,config/indexer/**/*.py,**/LeanKernel.Tests.*/*,**/*.g.cs,**/*.Designer.cs,**/*.razor,**/Program.cs,**/Data/Migrations/*.cs,**/Services/Auth/AuthRegistration.cs,**/Services/Auth/OidcRegistration.cs,**/Services/Auth/BearerTokenAuthHandler.cs,**/Services/EngagementAuthorizationFilter.cs,**/Services/ChannelInitializationService.cs,**/Services/Skills/SkillHostedService.cs,**/Services/AttachmentTextExtractionService.cs,**/Services/EngagementRulesProvider.cs,**/LeanKernel.Commander/Adapters/SignalRestApiAdapter.cs,**/LeanKernel.Plugins/BuiltIn/Skills/DynamicSkillTool.cs,**/LeanKernel.Plugins/BuiltIn/Skills/BinaryResolver.cs,**/LeanKernel.Plugins/BuiltIn/Skills/DynamicSkillToolFactory.cs,**/LeanKernel.Plugins/BuiltIn/Skills/EgressPolicy.cs,**/LeanKernel.Plugins/BuiltIn/Skills/RuntimeSkillRegistry.cs,**/LeanKernel.Generators/ToolRegistryGenerator.cs"
    dotnet restore src/LeanKernel.sln
    dotnet build src/LeanKernel.sln -c Release --no-restore
    dotnet test src/LeanKernel.sln -c Release --no-build \
      --collect:"XPlat Code Coverage" \
      --settings src/LeanKernel.Tests.Unit/coverage.sonar.runsettings \
      --results-directory coverage-results/sonar \
      --filter "FullyQualifiedName!~Integration"
    dotnet sonarscanner end /d:sonar.token="$SONAR_TOKEN"
  '

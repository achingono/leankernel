#!/usr/bin/env bash
set -e
mkdir -p sonar-reports

if [ -z "${SONAR_TOKEN}" ]; then
  echo "::warning title=SonarQube summary skipped::SONAR_TOKEN missing because setup step failed"
  echo '{"total":0,"issues":[],"components":[],"facets":[]}' > sonar-reports/issues.json
  exit 0
fi

curl_retry_common=(--retry 8 --retry-delay 2 --retry-all-errors)

# Wait for background compute task so issues API reflects this analysis.
if [ -f ".scannerwork/report-task.txt" ]; then
  ce_task_id=$(awk -F= '/^ceTaskId=/{print $2}' .scannerwork/report-task.txt)

  if [ -n "$ce_task_id" ]; then
    for i in $(seq 1 60); do
      ce_status=$(curl -fsS "${curl_retry_common[@]}" -u "${SONAR_TOKEN}:" \
        "${SONAR_HOST_URL}/api/ce/task?id=${ce_task_id}" | jq -r '.task.status // "UNKNOWN"')

      if [ "$ce_status" = "SUCCESS" ]; then
        break
      fi

      if [ "$ce_status" = "FAILED" ] || [ "$ce_status" = "CANCELED" ]; then
        echo "::error title=SonarQube compute task failed::ceTaskId=${ce_task_id}, status=${ce_status}"
        exit 1
      fi

      if [ "$i" -eq 60 ]; then
        echo "::warning title=SonarQube compute task timeout::ceTaskId=${ce_task_id} did not complete within wait window"
      else
        sleep 2
      fi
    done
  fi
fi

# Sonar API parameters vary by version. Probe multiple query variants without
# hard-failing on 4xx so we can gracefully fall back.
fetch_issues() {
  local url="$1"
  local label="$2"
  local tmp
  tmp="$(mktemp)"

  http_code=$(curl -sS -u "${SONAR_TOKEN}:" -o "$tmp" -w "%{http_code}" "$url" || true)

  if [ "$http_code" = "200" ] && jq -e '.issues != null and .total != null' "$tmp" >/dev/null 2>&1; then
    mv "$tmp" sonar-reports/issues.json
    echo "::notice title=SonarQube issues API::Using ${label} query parameters"
    return 0
  fi

  echo "::warning title=SonarQube issues API fallback::${label} query failed (HTTP ${http_code}), trying fallback"
  rm -f "$tmp"
  return 1
}

if ! fetch_issues \
  "${SONAR_HOST_URL}/api/issues/search?projectKeys=${SONAR_PROJECT_KEY}&issueStatuses=OPEN&ps=500" \
  "issueStatuses"; then
  fetch_issues \
    "${SONAR_HOST_URL}/api/issues/search?projectKeys=${SONAR_PROJECT_KEY}&statuses=OPEN&resolved=false&ps=500" \
    "legacy statuses" || {
      echo "::error title=SonarQube issues API failed::Could not retrieve issues from SonarQube using either query format"
      exit 1
    }
fi

total=$(jq -r '.total // 0' sonar-reports/issues.json)

blocker=$(jq '[.issues[] | select(.severity=="BLOCKER")] | length' sonar-reports/issues.json)
critical=$(jq '[.issues[] | select(.severity=="CRITICAL")] | length' sonar-reports/issues.json)
major=$(jq '[.issues[] | select(.severity=="MAJOR")] | length' sonar-reports/issues.json)
minor=$(jq '[.issues[] | select(.severity=="MINOR")] | length' sonar-reports/issues.json)
info=$(jq '[.issues[] | select(.severity=="INFO")] | length' sonar-reports/issues.json)

bugs=$(jq '[.issues[] | select(.type=="BUG")] | length' sonar-reports/issues.json)
vulns=$(jq '[.issues[] | select(.type=="VULNERABILITY")] | length' sonar-reports/issues.json)
hotspots=$(jq '[.issues[] | select(.type=="SECURITY_HOTSPOT")] | length' sonar-reports/issues.json)
smells=$(jq '[.issues[] | select(.type=="CODE_SMELL")] | length' sonar-reports/issues.json)

echo "## SonarQube Scan Summary" >> $GITHUB_STEP_SUMMARY
echo "" >> $GITHUB_STEP_SUMMARY
echo "| Metric | Count |" >> $GITHUB_STEP_SUMMARY
echo "| --- | ---: |" >> $GITHUB_STEP_SUMMARY
echo "| Total Open Issues | ${total} |" >> $GITHUB_STEP_SUMMARY
echo "| Blocker | ${blocker} |" >> $GITHUB_STEP_SUMMARY
echo "| Critical | ${critical} |" >> $GITHUB_STEP_SUMMARY
echo "| Major | ${major} |" >> $GITHUB_STEP_SUMMARY
echo "| Minor | ${minor} |" >> $GITHUB_STEP_SUMMARY
echo "| Info | ${info} |" >> $GITHUB_STEP_SUMMARY
echo "" >> $GITHUB_STEP_SUMMARY
echo "| Type | Count |" >> $GITHUB_STEP_SUMMARY
echo "| --- | ---: |" >> $GITHUB_STEP_SUMMARY
echo "| Bug | ${bugs} |" >> $GITHUB_STEP_SUMMARY
echo "| Vulnerability | ${vulns} |" >> $GITHUB_STEP_SUMMARY
echo "| Security Hotspot | ${hotspots} |" >> $GITHUB_STEP_SUMMARY
echo "| Code Smell | ${smells} |" >> $GITHUB_STEP_SUMMARY

if [ "$blocker" -gt 0 ] || [ "$critical" -gt 0 ]; then
  echo "::warning title=SonarQube summary::Open issues=${total}; blocker=${blocker}; critical=${critical}; major=${major}; vulnerabilities=${vulns}; hotspots=${hotspots}"
else
  echo "::notice title=SonarQube summary::Open issues=${total}; major=${major}; vulnerabilities=${vulns}; hotspots=${hotspots}"
fi

cat > sonar-reports/pr-comment.md <<EOF
<!-- sonar-scan-summary -->
## SonarQube Scan Summary

| Metric | Count |
| --- | ---: |
| Total Open Issues | ${total} |
| Blocker | ${blocker} |
| Critical | ${critical} |
| Major | ${major} |
| Minor | ${minor} |
| Info | ${info} |

| Type | Count |
| --- | ---: |
| Bug | ${bugs} |
| Vulnerability | ${vulns} |
| Security Hotspot | ${hotspots} |
| Code Smell | ${smells} |

Run: ${GITHUB_SERVER_URL}/${GITHUB_REPOSITORY}/actions/runs/${GITHUB_RUN_ID}
EOF

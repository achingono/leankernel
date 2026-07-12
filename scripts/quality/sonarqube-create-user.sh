#!/usr/bin/env bash
set -euo pipefail

# Usage:
#   ./sonarqube-create-user.sh --login <login> --password <password> [--name <name>] [--admin]
#
# Environment variable overrides:
#   SONAR_HOST_URL     – SonarQube base URL  (default: http://localhost:9000)
#   SONAR_LOGIN        – Admin login          (default: admin)
#   SONAR_PASSWORD     – Admin password       (default: admin)

SONAR_HOST_URL="${SONAR_HOST_URL:-http://localhost:9000}"
SONAR_LOGIN="${SONAR_LOGIN:-admin}"
SONAR_PASSWORD="${SONAR_PASSWORD:-admin}"

NEW_LOGIN=""
NEW_PASSWORD=""
NEW_NAME=""
MAKE_ADMIN=false

usage() {
  echo "Usage: $0 --login <login> --password <password> [--name <name>] [--admin]" >&2
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --login)    NEW_LOGIN="$2";    shift 2 ;;
    --password) NEW_PASSWORD="$2"; shift 2 ;;
    --name)     NEW_NAME="$2";     shift 2 ;;
    --admin)    MAKE_ADMIN=true;   shift   ;;
    *) usage ;;
  esac
done

[[ -z "$NEW_LOGIN"    ]] && { echo "Error: --login is required."    >&2; usage; }
[[ -z "$NEW_PASSWORD" ]] && { echo "Error: --password is required." >&2; usage; }
[[ -z "$NEW_NAME"     ]] && NEW_NAME="$NEW_LOGIN"

echo "Creating user '$NEW_LOGIN' on $SONAR_HOST_URL ..."
http_code="$(curl -sS -o /tmp/sonar_create_user.json -w '%{http_code}' \
  -u "$SONAR_LOGIN:$SONAR_PASSWORD" \
  -X POST "$SONAR_HOST_URL/api/users/create" \
  --data-urlencode "login=$NEW_LOGIN" \
  --data-urlencode "name=$NEW_NAME" \
  --data-urlencode "password=$NEW_PASSWORD")"

if [[ "$http_code" != "200" ]]; then
  echo "Error: SonarQube returned HTTP $http_code" >&2
  cat /tmp/sonar_create_user.json >&2
  echo "" >&2
  exit 1
fi

python3 - /tmp/sonar_create_user.json <<'PYEOF'
import json, sys
with open(sys.argv[1]) as f:
    u = json.load(f).get('user', {})
print('  login : ' + str(u.get('login')))
print('  name  : ' + str(u.get('name')))
print('  active: ' + str(u.get('active')))
PYEOF

if [[ "$MAKE_ADMIN" == "true" ]]; then
  echo "Adding '$NEW_LOGIN' to sonar-administrators ..."
  http_code="$(curl -sS -o /tmp/sonar_add_group.json -w '%{http_code}' \
    -u "$SONAR_LOGIN:$SONAR_PASSWORD" \
    -X POST "$SONAR_HOST_URL/api/user_groups/add_user" \
    --data-urlencode "name=sonar-administrators" \
    --data-urlencode "login=$NEW_LOGIN")"
  if [[ "$http_code" != "204" && "$http_code" != "200" ]]; then
    echo "Error: could not add user to sonar-administrators (HTTP $http_code)" >&2
    cat /tmp/sonar_add_group.json >&2
    echo "" >&2
    exit 1
  fi
  echo "  Done – '$NEW_LOGIN' is now an administrator."
fi

echo "User '$NEW_LOGIN' created successfully."

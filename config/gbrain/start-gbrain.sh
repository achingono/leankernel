#!/usr/bin/env sh
set -eu

if [ -n "${GBRAIN_DB_URL:-}" ]; then
  db_url="${GBRAIN_DB_URL}"
else
  encoded_password="$(bun -e "console.log(encodeURIComponent(process.env.POSTGRES_PASSWORD || ''))")"
  db_url="postgresql://${POSTGRES_USER:-leankernel}:${encoded_password}@${POSTGRES_HOST:-database}:${POSTGRES_PORT:-5432}/${POSTGRES_DB:-leankernel}"
fi

if [ -z "${OPENAI_BASE_URL:-}" ] && [ -n "${LITELLM_BASE_URL:-}" ]; then
  OPENAI_BASE_URL="${LITELLM_BASE_URL%/}/v1"
  export OPENAI_BASE_URL
fi

if [ -z "${OPENAI_API_KEY:-}" ] && [ -n "${LITELLM_API_KEY:-}" ]; then
  OPENAI_API_KEY="${LITELLM_API_KEY}"
  export OPENAI_API_KEY
fi

gbrain init --url "${db_url}" --embedding-model "${GBRAIN_EMBEDDING_MODEL:-openai:embedding-small}"

# Create a bearer token for the engine if one doesn't already exist
TOKEN_FILE="/app/data/wiki/.engine-token"
if [ ! -f "$TOKEN_FILE" ]; then
  token=$(gbrain auth create leankernel-engine --takes-holders world 2>&1 | grep "^  gbrain_" | tr -d ' ')
  if [ -n "$token" ]; then
    printf '%s' "$token" > "$TOKEN_FILE"
  fi
fi

exec gbrain serve --http --bind "${GBRAIN_BIND:-0.0.0.0}" --port "${GBRAIN_PORT:-8789}" --enable-dcr
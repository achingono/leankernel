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

set_config_if_unset() {
  key="$1"
  value="$2"

  if gbrain config get "$key" >/dev/null 2>&1; then
    return 0
  fi

  if gbrain config set "$key" "$value" >/dev/null 2>&1; then
    echo "Initialized gbrain config $key=$value"
  else
    echo "WARN: failed to initialize gbrain config $key" >&2
  fi
}

gbrain init \
  --url "${db_url}" \
  --embedding-model "${GBRAIN_EMBEDDING_MODEL:-openai:embedding-small}" \
  --embedding-dimensions "${GBRAIN_EMBEDDING_DIMENSIONS:-3072}"

if [ -n "${LITELLM_BASE_URL:-}" ]; then
  reasoning_model="${GBRAIN_DREAM_REASONING_MODEL:-openai:medium}"
  utility_model="${GBRAIN_DREAM_UTILITY_MODEL:-openai:small}"

  if [ -z "${GBRAIN_MODEL:-}" ]; then
    GBRAIN_MODEL="${reasoning_model}"
    export GBRAIN_MODEL
  fi

  set_config_if_unset "models.dream.synthesize" "$reasoning_model"
  set_config_if_unset "models.dream.patterns" "$reasoning_model"
  set_config_if_unset "models.dream.synthesize_verdict" "$utility_model"
  set_config_if_unset "facts.extraction_model" "$reasoning_model"
fi

# Create a bearer token for the engine if one doesn't already exist.
# The token is shared with the engine through a dedicated shared volume.
TOKEN_FILE="/app/data/gbrain/.engine-token"
mkdir -p "$(dirname "$TOKEN_FILE")"
if [ ! -f "$TOKEN_FILE" ]; then
  token=$(gbrain auth create leankernel-engine --takes-holders world 2>&1 | grep "^  gbrain_" | tr -d ' ')
  if [ -n "$token" ]; then
    printf '%s' "$token" > "$TOKEN_FILE"
  fi
fi

exec gbrain serve --http --bind "${GBRAIN_BIND:-0.0.0.0}" --port "${GBRAIN_PORT:-8789}" --enable-dcr

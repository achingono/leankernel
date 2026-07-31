#!/usr/bin/env bash
# Imports the remote swarm gbrain data set (pages, content_chunks, page_versions, tags,
# page_aliases) into the local gbrain database with canonical slug rewriting:
#   A: learning/facts/{t}/{p}/{seq} -> memory/{LT}/{LP}/{CH_MEM}/learning/facts/{t}/{p}/{seq}
#   B: doc/{title}                  -> documents/{LT}/user/00000000-0000-0000-0000-000000000000/{LU}/{sha256(compiled_truth)}
#   C: {raw slug}                   -> memory/{LT}/{LP}/{CH_MEM}/{raw slug}
# Duplicate byte-identical doc variants merge (newest updated_at wins); remote embeddings are
# dropped and regenerated locally (R5). Run against gbrain_import_staging first (default).
#
# Usage: import-remote.sh [--staging|--real] [--apply] [--skip-embed]
#   --staging  (default) target gbrain_import_staging; safe to inspect without --apply
#   --real     target the real gbrain DB; requires --apply; takes pre- and post-import backups
#   --apply    execute the rewrite; without it, only dry-run + manifest + analysis
#   --skip-embed skip the embedding migration step (staging iteration only)
#
# Required env: GBRAIN_SECRETS_DIR (path to swarm deploy secrets dir; creds are copied into the
# database container and never echoed; optional overrides: GBRAIN_IMPORT_TENANT, GBRAIN_IMPORT_PERSON,
# GBRAIN_IMPORT_USER, GBRAIN_IMPORT_CHANNEL).
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SQL_DIR="$ROOT_DIR/scripts/gbrain/sql"
SQL_DIR_REMOTE=/tmp/gbrain-import-sql
BACKUP_DIR="$ROOT_DIR/scripts/gbrain/backups"
DB_CONTAINER=leankernel-database
GBRAIN_CONTAINER=leankernel-gbrain
REMOTE_HOST=192.168.1.5
STAGING_DB=gbrain_import_staging

usage() {
  cat <<'EOF'
Usage: import-remote.sh [--staging|--real] [--apply] [--skip-embed]

Modes (default: --staging):
  --staging  target gbrain_import_staging DB (safe; no --apply needed to inspect)
  --real     target the real gbrain DB (requires --apply; pre/post-import backups taken)

Options:
  --apply      execute the rewrite; without it, only dry-run + manifest
  --skip-embed skip the embedding migration step (staging iteration only)
  --help
EOF
}

MODE=staging
APPLY=0
SKIP_EMBED=0
for arg in "$@"; do
  case "$arg" in
    --real) MODE=real ;;
    --staging) MODE=staging ;;
    --apply) APPLY=1 ;;
    --skip-embed) SKIP_EMBED=1 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "unknown argument: $arg" >&2; usage; exit 2 ;;
  esac
done

if [ "$MODE" = real ] && [ "$APPLY" -ne 1 ]; then
  echo "refusing: --real requires --apply (protects the production gbrain DB)" >&2
  exit 2
fi

if [ -z "${GBRAIN_SECRETS_DIR:-}" ] || [ ! -f "$GBRAIN_SECRETS_DIR/postgres_user.txt" ] || [ ! -f "$GBRAIN_SECRETS_DIR/postgres_password.txt" ]; then
  echo "error: set GBRAIN_SECRETS_DIR to the swarm secrets dir containing postgres_user.txt and postgres_password.txt" >&2
  exit 2
fi

for c in "$DB_CONTAINER" "$GBRAIN_CONTAINER"; do
  if ! docker inspect "$c" >/dev/null 2>&1; then
    echo "error: container $c is not running" >&2
    exit 1
  fi
done

mkdir -p "$BACKUP_DIR/manifests"

# --- credential handling: in-container copies only, removed on exit ---
copy_remote_creds() {
  docker cp "$GBRAIN_SECRETS_DIR/postgres_user.txt" "$DB_CONTAINER":/tmp/imp_user.txt
  docker cp "$GBRAIN_SECRETS_DIR/postgres_password.txt" "$DB_CONTAINER":/tmp/imp_pw.txt
  docker exec "$DB_CONTAINER" sh -c 'mkdir -p /tmp/gbrain-import-sql'
  for f in "$SQL_DIR"/*.sql; do docker cp "$f" "$DB_CONTAINER":/tmp/gbrain-import-sql/; done
}
cleanup() {
  docker exec "$DB_CONTAINER" rm -f /tmp/imp_user.txt /tmp/imp_pw.txt /tmp/gbrain-import-manifest.csv >/dev/null 2>&1 || true
  docker exec "$DB_CONTAINER" sh -c 'rm -rf /tmp/gbrain-import-data' >/dev/null 2>&1 || true
  rm -rf /tmp/gbrain/import >/dev/null 2>&1 || true
}
copy_remote_creds
trap cleanup EXIT

PSQL() { docker exec -i "$DB_CONTAINER" psql -U leankernel -q -v ON_ERROR_STOP=1 "$@"; }
# gbrain CLI commands run inside the gbrain container; for staging, point the CLI at the
# staging DB by composing the URL from the container's own POSTGRES_* env at runtime
STAGING_CMD_PREFIX='export GBRAIN_DATABASE_URL="postgresql://${POSTGRES_USER}:${POSTGRES_PASSWORD}@${POSTGRES_HOST}:${POSTGRES_PORT:-5432}/'$STAGING_DB'";'

echo "== step 1/10: resolve local identity =="
IDENTITY_ROW=$(docker exec "$DB_CONTAINER" psql -U leankernel -d gbrain -tA \
  -c "SELECT split_part(slug,'/',2), split_part(slug,'/',3), split_part(slug,'/',4) FROM pages WHERE slug LIKE 'memory/%/%/%/%/%' ORDER BY slug LIMIT 1;" 2>/dev/null || true)
if [ -z "$IDENTITY_ROW" ]; then
  echo "error: no local memory slug found to derive identity; set GBRAIN_IMPORT_TENANT/PERSON/CHANNEL/USER" >&2
  exit 1
fi
IFS='|' read -r LT_IMPLIED LP_IMPLIED CH_IMPLIED <<<"$IDENTITY_ROW"
LT="${GBRAIN_IMPORT_TENANT:-$LT_IMPLIED}"
LP="${GBRAIN_IMPORT_PERSON:-$LP_IMPLIED}"
CH_MEM="${GBRAIN_IMPORT_CHANNEL:-$CH_IMPLIED}"
LU="${GBRAIN_IMPORT_USER:-$(docker exec "$DB_CONTAINER" psql -U leankernel -d leankernel -tA -c "SELECT \"Id\" FROM \"Users\" WHERE \"PersonId\" = '$LP' AND \"IsGuest\" = false ORDER BY \"CreatedOn\" LIMIT 1;")}"
if [ -z "$LT" ] || [ -z "$LP" ] || [ -z "$LU" ] || [ -z "$CH_MEM" ]; then
  echo "error: could not resolve identity (tenant=$LT person=$LP user=$LU channel=$CH_MEM)" >&2
  exit 1
fi
echo "tenant=$LT person=$LP user=$LU channel=$CH_MEM"

TS=$(date +%Y%m%d-%H%M%S)
TARGET_DB=$([ "$MODE" = real ] && echo gbrain || echo "$STAGING_DB")

if [ "$MODE" = real ]; then
  echo "== step 2/10: pre-import backup of local gbrain =="
  docker exec "$DB_CONTAINER" pg_dump -U leankernel -d gbrain --format=plain --no-owner | gzip > "$BACKUP_DIR/gbrain-pre-import-$TS.sql.gz"
  echo "wrote $BACKUP_DIR/gbrain-pre-import-$TS.sql.gz"
else
  echo "== step 2/10: staging database =="
  PSQL -d postgres -c "DROP DATABASE IF EXISTS $STAGING_DB;" -c "CREATE DATABASE $STAGING_DB OWNER leankernel;"
  docker exec "$DB_CONTAINER" pg_dump -U leankernel -d gbrain --schema-only --no-owner | docker exec -i "$DB_CONTAINER" psql -U leankernel -q -v ON_ERROR_STOP=1 -d "$STAGING_DB"
  docker exec "$GBRAIN_CONTAINER" sh -c "$STAGING_CMD_PREFIX gbrain stats --json >/dev/null"
  echo "staging database ready (schema v$(docker exec "$DB_CONTAINER" psql -U leankernel -d $STAGING_DB -tAc "select value from config where key='version';"))"
fi

echo "== step 3/10: export remote tables =="
REMOTE_DUMP="$BACKUP_DIR/remote-gbrain-$TS.sql"
docker exec "$DB_CONTAINER" sh -c 'PGPASSWORD="$(cat /tmp/imp_pw.txt)" /usr/bin/pg_dump -h '"$REMOTE_HOST"' -U "$(cat /tmp/imp_user.txt)" -d leankernel --data-only --no-owner --table=pages --table=content_chunks --table=page_versions --table=tags --table=page_aliases' | gzip > "$REMOTE_DUMP.gz"
echo "wrote $REMOTE_DUMP.gz ($(du -h "$REMOTE_DUMP.gz" | cut -f1))"

echo "== step 4/10: restore into $TARGET_DB =="
if [ "$MODE" = real ]; then
  # The live DB already has local rows in the remote id space (1..N): load the dump into
  # per-table files, then re-insert with an id offset via temp tables (restore-real.sql).
  IMPORT_DATA_DIR=/tmp/gbrain/import
  rm -rf "$IMPORT_DATA_DIR" && mkdir -p "$IMPORT_DATA_DIR"
  gunzip -c "$REMOTE_DUMP.gz" | awk '
    /^\\\.$/ { in_t = ""; next }
    in_t != "" { print > ("'"$IMPORT_DATA_DIR"'/" in_t ".txt"); next }
    /^COPY public\./ { in_t = substr($0, 13); sub(/ .*/, "", in_t); next }
  '
  docker exec "$DB_CONTAINER" sh -c 'mkdir -p /tmp/gbrain-import-data'
  for f in "$IMPORT_DATA_DIR"/*.txt; do docker cp "$f" "$DB_CONTAINER":/tmp/gbrain-import-data/; done
  PSQL -d "$TARGET_DB" -f "$SQL_DIR_REMOTE/restore-real.sql"
else
  gunzip -c "$REMOTE_DUMP.gz" | docker exec -i "$DB_CONTAINER" psql -U leankernel -q -v ON_ERROR_STOP=1 -d "$TARGET_DB"
fi
PSQL -d "$TARGET_DB" -f "$SQL_DIR_REMOTE/sequence-fixup.sql" >/dev/null
echo "restored: $(docker exec "$DB_CONTAINER" psql -U leankernel -d "$TARGET_DB" -tAc 'select count(*) from pages;') pages, $(docker exec "$DB_CONTAINER" psql -U leankernel -d "$TARGET_DB" -tAc 'select count(*) from content_chunks;') chunks, $(docker exec "$DB_CONTAINER" psql -U leankernel -d "$TARGET_DB" -tAc 'select count(*) from tags;') tags, $(docker exec "$DB_CONTAINER" psql -U leankernel -d "$TARGET_DB" -tAc 'select count(*) from page_aliases;') aliases"

echo "== step 5/10: compute rewrite plan (manifest) =="
MANIFEST="$BACKUP_DIR/manifests/rewrite-manifest-$MODE-$TS.csv"
{
  printf 'old_slug,new_slug,type,channel_copy,collision_strategy,action,updated_at\n'
  docker exec "$DB_CONTAINER" psql -U leankernel -d "$TARGET_DB" -At -F',' -v ON_ERROR_STOP=1 \
    -v LT="$LT" -v LP="$LP" -v LU="$LU" -v CH_MEM="$CH_MEM" -f "$SQL_DIR_REMOTE/manifest.sql"
} > "$MANIFEST"
echo "wrote $MANIFEST ($(($(wc -l < "$MANIFEST") - 1)) rows)"

echo "== step 6/10: dry-run analysis =="
docker cp "$MANIFEST" "$DB_CONTAINER":/tmp/gbrain-import-manifest.csv
PSQL -d "$TARGET_DB" -f "$SQL_DIR_REMOTE/analyze-manifest.sql"

if [ "$APPLY" -ne 1 ]; then
  echo
  echo "dry-run complete: manifest at $MANIFEST; rerun with --apply to execute the rewrite (MODE=$MODE)"
  exit 0
fi

echo "== step 7/10: apply rewrite + merge + drop remote embeddings =="
docker cp "$MANIFEST" "$DB_CONTAINER":/tmp/gbrain-import-manifest.csv
PSQL -d "$TARGET_DB" -f "$SQL_DIR_REMOTE/rewrite-pages.sql"
echo "rewrite applied"

echo "== step 8/10: validation =="
PSQL -d "$TARGET_DB" -f "$SQL_DIR_REMOTE/validate-import.sql"

if [ "$SKIP_EMBED" -ne 1 ]; then
  echo "== step 9/10: re-embed with openai:embedding =="
  # OPENAI_API_KEY/BASE_URL default empty in the gbrain container; point them at the
  # LiteLLM proxy (container env already carries LITELLM_* for the MCP server)
  GBRAIN_CMD='export OPENAI_API_KEY="${LITELLM_API_KEY:-sk-leankernel-local}" OPENAI_BASE_URL="${LITELLM_BASE_URL:-http://litellm:4000}/v1"; gbrain migrate embeddings --to openai:embedding --dim 3072 --dry-run --json; gbrain migrate embeddings --to openai:embedding --dim 3072 --yes --json'
  if [ "$MODE" = staging ]; then
    docker exec "$GBRAIN_CONTAINER" sh -c "$STAGING_CMD_PREFIX $GBRAIN_CMD"
  else
    # Pin the CLI to the real DB explicitly: the container's ~/.gbrain/config.json
    # may point at another DB (e.g. a prior staging session), which would make
    # the plan silently see the wrong database.
    REAL_CMD_PREFIX='export GBRAIN_DATABASE_URL="postgresql://${POSTGRES_USER}:${POSTGRES_PASSWORD}@${POSTGRES_HOST}:${POSTGRES_PORT:-5432}/gbrain";'
    docker exec "$GBRAIN_CONTAINER" sh -c "$REAL_CMD_PREFIX $GBRAIN_CMD"
  fi
  echo "embeddings regenerated: $(docker exec "$DB_CONTAINER" psql -U leankernel -d "$TARGET_DB" -tAc 'select count(*) from content_chunks where embedding is not null;') chunks embedded"
  PSQL -d "$TARGET_DB" -f "$SQL_DIR_REMOTE/validate-import.sql" | grep -E 'embedding (null|model)'
else
  echo "skipped embedding migration (--skip-embed); embeddings remain NULL for $(docker exec "$DB_CONTAINER" psql -U leankernel -d "$TARGET_DB" -tAc 'select count(*) from content_chunks where embedding is null;') chunks"
fi

if [ "$MODE" = real ]; then
  echo "== post-import backup of local gbrain =="
  docker exec "$DB_CONTAINER" pg_dump -U leankernel -d gbrain --format=plain --no-owner | gzip > "$BACKUP_DIR/gbrain-post-import-$TS.sql.gz"
  echo "wrote $BACKUP_DIR/gbrain-post-import-$TS.sql.gz"
fi

echo "done ($MODE); manifest: $MANIFEST"

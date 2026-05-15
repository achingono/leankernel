#!/usr/bin/env bash
# Fix invalid wiki references using LLM
# Converts OpenClaw-style paths (../domain/page.md) to LeanKernel entry references

set -euo pipefail

# Configuration
API_URL="${API_URL:-http://localhost:5080}"
OLLAMA_URL="${OLLAMA_URL:-http://localhost:11434}"
TOKEN="${1:-}"
DRY_RUN="${DRY_RUN:-true}"
VERBOSE="${VERBOSE:-false}"

if [ -z "$TOKEN" ]; then
    echo "Usage: $0 <token>"
    exit 1
fi

# Helper to extract references from text
extract_references() {
    local text="$1"
    # Match patterns like ../domain/page.md or ./domain/page.md
    echo "$text" | grep -oE '\.\./[a-zA-Z0-9_-]+/[a-zA-Z0-9_-]+\.md' || true
}

# Get all entries
echo "🔍 Scanning for invalid references..."
ENTRIES=$(curl -sS -H "Authorization: Bearer $TOKEN" "${API_URL}/api/wiki/entries")

# Process each entry
REFS_FOUND=0
ENTRIES_WITH_REFS=0

echo "$ENTRIES" | jq -r '.[] | 
  "\(.subject) (\(.dimension)) [\(.id // "\(.dimension)-\(.subject)")]: \(.facts | length) facts"' | while read -r entry_header; do
    
    # Skip if no facts
    if ! echo "$entry_header" | grep -q "facts"; then
        continue
    fi
    
    echo "$ENTRIES" | jq -c '.[] | 
      select(.subject | test("'"$(echo "$entry_header" | cut -d' ' -f1)"'")) |
      .facts[] | 
      select((.claim | test("\\.\\./")) or 
             ((.context.who // "") | test("\\.\\./")) or
             ((.context.what // "") | test("\\.\\./")) or
             ((.context.when // "") | test("\\.\\./")) or
             ((.context.where // "") | test("\\.\\./")) or
             ((.context.why // "") | test("\\.\\./")) or
             ((.context.how // "") | test("\\./"))')' | while read -r fact; do
        
        if [ -z "$fact" ]; then
            continue
        fi
        
        CLAIM=$(echo "$fact" | jq -r '.claim' 2>/dev/null || echo "")
        
        # Extract all references from claim and context
        REFS=$(echo "$fact" | jq -r '.claim, .context[] // empty' 2>/dev/null | grep -oE '\.\./[a-zA-Z0-9_-]+/[a-zA-Z0-9_-]+\.md' | sort -u || true)
        
        if [ -n "$REFS" ]; then
            ENTRIES_WITH_REFS=$((ENTRIES_WITH_REFS + 1))
            
            if [ "$VERBOSE" = "true" ]; then
                echo "📝 Entry: $(echo "$entry_header" | cut -d: -f1)"
                echo "   Claim: ${CLAIM:0:80}..."
            fi
            
            while read -r ref; do
                if [ -z "$ref" ]; then
                    continue
                fi
                
                REFS_FOUND=$((REFS_FOUND + 1))
                
                if [ "$VERBOSE" = "true" ]; then
                    echo "   Reference: $ref"
                fi
                
                # Extract domain and page
                DOMAIN=$(echo "$ref" | sed -E 's|^\.\./([a-zA-Z0-9_-]+)/.*|\1|')
                PAGE=$(echo "$ref" | sed -E 's|^\.\./.*/([a-zA-Z0-9_-]+)\.md|\1|')
                
                # Map to LeanKernel dimension
                case "$DOMAIN" in
                    career) DIMENSION="what" ;;
                    context) DIMENSION="where" ;;
                    financial) DIMENSION="how" ;;
                    identity|relationships) DIMENSION="who" ;;
                    ventures) DIMENSION="what" ;;
                    wisdom) DIMENSION="why" ;;
                    *) DIMENSION="what" ;;
                esac
                
                NORMALIZED="${DIMENSION}-${PAGE}"
                
                echo "   ✓ $ref -> $NORMALIZED"
            done <<< "$REFS"
        fi
    done
done

echo ""
echo "📊 Summary:"
echo "  Entries with invalid references: $ENTRIES_WITH_REFS"
echo "  Total invalid references found: $REFS_FOUND"

if [ "$DRY_RUN" = "true" ]; then
    echo "  (Dry run - no changes applied)"
fi

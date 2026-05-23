#!/usr/bin/env bash
# Wiki Backup/Restore Script for LeanKernel
# Usage:
#   backup:  ./wiki-backup.sh backup [destination]
#   restore: ./wiki-backup.sh restore <backup-file>

set -euo pipefail

WIKI_DIR="${LEANKERNEL_WIKI_PATH:-./data/wiki}"
BACKUP_DIR="${LEANKERNEL_BACKUP_DIR:-./data/backups}"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)

usage() {
    echo "LeanKernel Wiki Backup/Restore"
    echo ""
    echo "Usage:"
    echo "  $0 backup  [destination-dir]   Create a timestamped backup"
    echo "  $0 restore <backup-file.tar.gz> Restore from backup"
    echo "  $0 list                         List available backups"
    echo ""
    echo "Environment:"
    echo "  LEANKERNEL_WIKI_PATH   Wiki directory (default: ./data/wiki)"
    echo "  LEANKERNEL_BACKUP_DIR  Backup directory (default: ./data/backups)"
}

backup() {
    local dest="${1:-$BACKUP_DIR}"
    mkdir -p "$dest"

    if [ ! -d "$WIKI_DIR" ]; then
        echo "Error: Wiki directory not found at $WIKI_DIR"
        exit 1
    fi

    local backup_file="$dest/LeanKernel-wiki-${TIMESTAMP}.tar.gz"

    # Count entries
    local count
    count=$(find "$WIKI_DIR" -name "*.md" -not -path "*/.meta/*" | wc -l)

    echo "Backing up wiki ($count entries) from $WIKI_DIR"
    tar -czf "$backup_file" -C "$(dirname "$WIKI_DIR")" "$(basename "$WIKI_DIR")"

    local size
    size=$(du -h "$backup_file" | cut -f1)
    echo "Backup created: $backup_file ($size)"
}

restore() {
    local backup_file="${1:-}"
    if [ -z "$backup_file" ] || [ ! -f "$backup_file" ]; then
        echo "Error: Backup file not found: $backup_file"
        exit 1
    fi

    echo "Warning: This will overwrite the current wiki at $WIKI_DIR"
    read -rp "Continue? (y/N) " confirm
    if [ "$confirm" != "y" ] && [ "$confirm" != "Y" ]; then
        echo "Aborted."
        exit 0
    fi

    # Create safety backup first
    if [ -d "$WIKI_DIR" ]; then
        local safety="$BACKUP_DIR/LeanKernel-wiki-pre-restore-${TIMESTAMP}.tar.gz"
        mkdir -p "$BACKUP_DIR"
        echo "Creating safety backup: $safety"
        tar -czf "$safety" -C "$(dirname "$WIKI_DIR")" "$(basename "$WIKI_DIR")"
    fi

    # Restore
    echo "Restoring from $backup_file"
    mkdir -p "$(dirname "$WIKI_DIR")"
    rm -rf "$WIKI_DIR"
    tar -xzf "$backup_file" -C "$(dirname "$WIKI_DIR")"

    local count
    count=$(find "$WIKI_DIR" -name "*.md" -not -path "*/.meta/*" | wc -l)
    echo "Restored $count wiki entries to $WIKI_DIR"
}

list_backups() {
    if [ ! -d "$BACKUP_DIR" ]; then
        echo "No backups found. Directory $BACKUP_DIR does not exist."
        return
    fi

    echo "Available backups in $BACKUP_DIR:"
    echo ""
    ls -lhS "$BACKUP_DIR"/LeanKernel-wiki-*.tar.gz 2>/dev/null || echo "  (none)"
}

case "${1:-}" in
    backup)  backup "${2:-}" ;;
    restore) restore "${2:-}" ;;
    list)    list_backups ;;
    *)       usage ;;
esac

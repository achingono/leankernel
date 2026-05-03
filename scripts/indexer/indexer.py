"""
LeanKernel Knowledge Indexer — Sidecar service for indexing wiki and documents.

Watches /data/wiki/ and /data/documents/ directories, processes files,
generates embeddings, and stores vectors in Qdrant.

Wiki .md files: parsed directly (frontmatter + body).
Document files: parsed via Unstructured.io API.
"""

import asyncio
import hashlib
import json
import logging
import os
import sqlite3
import time
from pathlib import Path
from typing import Optional

import httpx
from qdrant_client import QdrantClient
from qdrant_client.models import (
    Distance,
    FieldCondition,
    Filter,
    MatchValue,
    PointStruct,
    VectorParams,
)
from watchdog.events import FileSystemEvent, FileSystemEventHandler
from watchdog.observers import Observer

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
)
logger = logging.getLogger("LeanKernel-indexer")

# Configuration from environment
UNSTRUCTURED_API_URL = os.getenv("UNSTRUCTURED_API_URL", "http://unstructured:8000")
LITELLM_URL = os.getenv("LITELLM_URL", "http://litellm:4000")
LITELLM_API_KEY = os.getenv("LITELLM_API_KEY", "sk-LeanKernel-local")
QDRANT_HOST = os.getenv("QDRANT_HOST", "qdrant")
QDRANT_PORT = int(os.getenv("QDRANT_PORT", "6334"))
COLLECTION_NAME = os.getenv("COLLECTION_NAME", "LEANKERNEL_knowledge")
WIKI_PATH = os.getenv("WIKI_PATH", "/app/data/wiki")
DOCUMENTS_PATH = os.getenv("DOCUMENTS_PATH", "/app/data/documents")
EMBEDDING_MODEL = os.getenv("EMBEDDING_MODEL", "embedding-small")
EMBEDDING_DIMENSION = int(os.getenv("EMBEDDING_DIMENSION", "1536"))
RESCAN_INTERVAL = int(os.getenv("RESCAN_INTERVAL_SECONDS", "300"))
WIKI_DEBOUNCE = int(os.getenv("WIKI_DEBOUNCE_SECONDS", "5"))
CHUNK_SIZE_TOKENS = int(os.getenv("CHUNK_SIZE_TOKENS", "500"))
STATE_DB_PATH = os.getenv("STATE_DB_PATH", "/app/data/indexer-state.db")
TAG_RULES_JSON = os.getenv("TAG_RULES_JSON", "[]")

# Document extensions that should go through Unstructured
UNSTRUCTURED_EXTENSIONS = {
    ".pdf", ".docx", ".doc", ".pptx", ".ppt", ".xlsx", ".xls",
    ".html", ".htm", ".epub", ".rtf", ".odt", ".csv", ".tsv",
    ".eml", ".msg", ".rst", ".org",
}

# Text-based extensions that can be read directly
TEXT_EXTENSIONS = {".md", ".txt", ".json", ".yaml", ".yml", ".xml", ".log"}


class StateDB:
    """SQLite state tracking for indexed files."""

    def __init__(self, db_path: str):
        os.makedirs(os.path.dirname(db_path), exist_ok=True)
        self.conn = sqlite3.connect(db_path)
        self.conn.execute("""
            CREATE TABLE IF NOT EXISTS indexed_files (
                file_path TEXT PRIMARY KEY,
                file_hash TEXT NOT NULL,
                indexed_at REAL NOT NULL,
                chunk_count INTEGER DEFAULT 0,
                tags TEXT DEFAULT '[]'
            )
        """)
        self.conn.commit()

    def get_file_hash(self, file_path: str) -> Optional[str]:
        row = self.conn.execute(
            "SELECT file_hash FROM indexed_files WHERE file_path = ?",
            (file_path,),
        ).fetchone()
        return row[0] if row else None

    def upsert(self, file_path: str, file_hash: str, chunk_count: int, tags: list[str]):
        self.conn.execute(
            """INSERT OR REPLACE INTO indexed_files
               (file_path, file_hash, indexed_at, chunk_count, tags)
               VALUES (?, ?, ?, ?, ?)""",
            (file_path, file_hash, time.time(), chunk_count, json.dumps(tags)),
        )
        self.conn.commit()

    def remove(self, file_path: str):
        self.conn.execute("DELETE FROM indexed_files WHERE file_path = ?", (file_path,))
        self.conn.commit()

    def get_all_paths(self) -> set[str]:
        rows = self.conn.execute("SELECT file_path FROM indexed_files").fetchall()
        return {row[0] for row in rows}


class TagResolver:
    """Resolves tags for files based on path-pattern rules."""

    def __init__(self, rules_json: str, default_tags: list[str] | None = None):
        self.rules = json.loads(rules_json) if rules_json else []
        self.default_tags = default_tags or ["general"]

    def resolve(self, relative_path: str) -> list[str]:
        """Resolve tags for a file based on its relative path."""
        from fnmatch import fnmatch

        tags = set()
        for rule in self.rules:
            pattern = rule.get("pathPattern", "")
            if fnmatch(relative_path, pattern):
                tags.update(rule.get("tags", []))

        if not tags:
            tags.update(self.default_tags)

        return sorted(tags)


class EmbeddingClient:
    """Generates embeddings via LiteLLM API."""

    def __init__(self, base_url: str, api_key: str, model: str):
        self.base_url = base_url.rstrip("/")
        self.api_key = api_key
        self.model = model
        self.client = httpx.AsyncClient(timeout=60.0)

    async def embed(self, text: str) -> list[float]:
        """Generate embedding for a single text."""
        response = await self.client.post(
            f"{self.base_url}/embeddings",
            json={"input": text, "model": self.model},
            headers={"Authorization": f"Bearer {self.api_key}"},
        )
        response.raise_for_status()
        data = response.json()
        return data["data"][0]["embedding"]

    async def close(self):
        await self.client.aclose()


class UnstructuredClient:
    """Parses documents via Unstructured.io API."""

    def __init__(self, base_url: str):
        self.base_url = base_url.rstrip("/")
        self.client = httpx.AsyncClient(timeout=300.0)

    async def parse(self, file_path: str) -> list[dict]:
        """Parse a document file and return chunked elements."""
        with open(file_path, "rb") as f:
            response = await self.client.post(
                f"{self.base_url}/general/v0/general",
                files={"files": (os.path.basename(file_path), f)},
                data={
                    "strategy": "auto",
                    "chunking_strategy": "by_title",
                    "max_characters": CHUNK_SIZE_TOKENS * 4,
                },
            )
        response.raise_for_status()
        return response.json()

    async def close(self):
        await self.client.aclose()


def parse_wiki_markdown(file_path: str) -> dict | None:
    """Parse a wiki markdown file with YAML frontmatter."""
    try:
        with open(file_path, "r", encoding="utf-8") as f:
            content = f.read()
    except (IOError, UnicodeDecodeError):
        return None

    lines = content.split("\n")
    if len(lines) < 3 or lines[0].strip() != "---":
        return None

    # Find end of frontmatter
    end_idx = -1
    for i in range(1, len(lines)):
        if lines[i].strip() == "---":
            end_idx = i
            break

    if end_idx < 0:
        return None

    # Parse frontmatter (simple key: value)
    frontmatter = {}
    for line in lines[1:end_idx]:
        colon_idx = line.find(":")
        if colon_idx > 0:
            key = line[:colon_idx].strip()
            value = line[colon_idx + 1:].strip()
            frontmatter[key] = value

    # Body is everything after frontmatter
    body_lines = lines[end_idx + 1:]
    body = "\n".join(body_lines).strip()

    # Extract facts (lines starting with "- ")
    facts = []
    for line in body_lines:
        line = line.strip()
        if line.startswith("- ") and not line.startswith("- ["):
            # Remove HTML comment metadata
            import re
            claim = re.sub(r"\s*<!--\{[^}]*\}-->$", "", line[2:])
            if claim:
                facts.append(claim)

    return {
        "id": frontmatter.get("id", ""),
        "dimension": frontmatter.get("dimension", ""),
        "subject": frontmatter.get("subject", ""),
        "body": body,
        "facts": facts,
        "text_for_embedding": f"{frontmatter.get('dimension', '')}:{frontmatter.get('subject', '')} — "
        + "; ".join(facts) if facts else body,
    }


def compute_file_hash(file_path: str) -> str:
    """Compute SHA-256 hash of a file."""
    h = hashlib.sha256()
    with open(file_path, "rb") as f:
        for chunk in iter(lambda: f.read(8192), b""):
            h.update(chunk)
    return h.hexdigest()


def chunk_text(text: str, max_tokens: int = 500) -> list[str]:
    """Split text into chunks of approximately max_tokens."""
    max_chars = max_tokens * 4  # ~4 chars per token
    if len(text) <= max_chars:
        return [text]

    chunks = []
    paragraphs = text.split("\n\n")
    current_chunk = ""

    for para in paragraphs:
        if len(current_chunk) + len(para) + 2 > max_chars:
            if current_chunk:
                chunks.append(current_chunk.strip())
            current_chunk = para
        else:
            current_chunk += "\n\n" + para if current_chunk else para

    if current_chunk.strip():
        chunks.append(current_chunk.strip())

    return chunks if chunks else [text[:max_chars]]


class Indexer:
    """Main indexing engine."""

    def __init__(self):
        self.state = StateDB(STATE_DB_PATH)
        self.tag_resolver = TagResolver(TAG_RULES_JSON, default_tags=["general"])
        self.embedding_client = EmbeddingClient(LITELLM_URL, LITELLM_API_KEY, EMBEDDING_MODEL)
        self.unstructured_client = UnstructuredClient(UNSTRUCTURED_API_URL)
        self.qdrant = QdrantClient(host=QDRANT_HOST, port=QDRANT_PORT, prefer_grpc=True)
        self._ensure_collection()

    def _ensure_collection(self):
        """Create Qdrant collection if it doesn't exist."""
        try:
            if not self.qdrant.collection_exists(COLLECTION_NAME):
                self.qdrant.create_collection(
                    collection_name=COLLECTION_NAME,
                    vectors_config=VectorParams(
                        size=EMBEDDING_DIMENSION,
                        distance=Distance.COSINE,
                    ),
                )
                logger.info(f"Created Qdrant collection: {COLLECTION_NAME}")
            else:
                logger.info(f"Qdrant collection exists: {COLLECTION_NAME}")
        except Exception as e:
            logger.error(f"Failed to connect to Qdrant: {e}")
            raise

    async def index_file(self, file_path: str):
        """Index a single file (wiki or document)."""
        if not os.path.isfile(file_path):
            return

        file_hash = compute_file_hash(file_path)
        stored_hash = self.state.get_file_hash(file_path)

        if stored_hash == file_hash:
            logger.debug(f"Skipping unchanged file: {file_path}")
            return

        # Determine relative path for tag resolution
        if file_path.startswith(WIKI_PATH):
            relative_path = "wiki/" + os.path.relpath(file_path, WIKI_PATH)
            source_type = "wiki"
        elif file_path.startswith(DOCUMENTS_PATH):
            relative_path = "documents/" + os.path.relpath(file_path, DOCUMENTS_PATH)
            source_type = "document"
        else:
            relative_path = os.path.basename(file_path)
            source_type = "document"

        tags = self.tag_resolver.resolve(relative_path)
        ext = os.path.splitext(file_path)[1].lower()

        try:
            if source_type == "wiki" and ext == ".md":
                chunks = await self._process_wiki_file(file_path)
            elif ext in UNSTRUCTURED_EXTENSIONS:
                chunks = await self._process_unstructured_file(file_path)
            elif ext in TEXT_EXTENSIONS:
                chunks = await self._process_text_file(file_path)
            else:
                logger.warning(f"Unsupported file type: {file_path}")
                return

            if not chunks:
                logger.warning(f"No content extracted from: {file_path}")
                return

            # Delete old vectors for this file (best-effort during re-index)
            try:
                self._delete_file_vectors(file_path)
            except Exception as e:
                logger.debug(f"Pre-index delete skipped for {file_path}: {e}")

            # Generate embeddings and upsert
            points = []
            expected_chunks = len(chunks)
            failed_chunks = 0
            for i, chunk_text_content in enumerate(chunks):
                try:
                    embedding = await self.embedding_client.embed(chunk_text_content)
                except Exception as e:
                    logger.error(f"Embedding failed for chunk {i} of {file_path}: {e}")
                    failed_chunks += 1
                    continue

                point_id = self._generate_point_id(file_path, i)
                payload = {
                    "source_type": source_type,
                    "source_file": relative_path,
                    "chunk_index": i,
                    "text": chunk_text_content,
                    "tags": tags,
                    "indexed_at": int(time.time()),
                }

                # Add wiki-specific metadata
                if source_type == "wiki":
                    wiki_data = parse_wiki_markdown(file_path)
                    if wiki_data:
                        payload["entry_id"] = wiki_data["id"]
                        payload["dimension"] = wiki_data["dimension"]
                        payload["subject"] = wiki_data["subject"]

                points.append(PointStruct(id=point_id, vector=embedding, payload=payload))

            if failed_chunks > 0 and not points:
                logger.error(f"All chunks failed for {file_path}, skipping state update")
                return

            if points:
                self.qdrant.upsert(collection_name=COLLECTION_NAME, points=points)
                logger.info(f"Indexed {len(points)}/{expected_chunks} chunks from: {relative_path}")

            if failed_chunks > 0:
                logger.warning(f"Partial indexing for {relative_path}: {failed_chunks}/{expected_chunks} chunks failed, not persisting state")
                return

            self.state.upsert(file_path, file_hash, len(points), tags)

        except Exception as e:
            logger.error(f"Failed to index {file_path}: {e}")

    async def _process_wiki_file(self, file_path: str) -> list[str]:
        """Process a wiki markdown file into text chunks."""
        wiki_data = parse_wiki_markdown(file_path)
        if not wiki_data:
            return []
        text = wiki_data["text_for_embedding"]
        return chunk_text(text, CHUNK_SIZE_TOKENS) if text else []

    async def _process_unstructured_file(self, file_path: str) -> list[str]:
        """Process a document through Unstructured API."""
        elements = await self.unstructured_client.parse(file_path)
        chunks = []
        for element in elements:
            text = element.get("text", "").strip()
            if text and len(text) > 20:
                chunks.append(text)
        return chunks

    async def _process_text_file(self, file_path: str) -> list[str]:
        """Process a plain text file directly."""
        try:
            with open(file_path, "r", encoding="utf-8") as f:
                content = f.read()
        except (IOError, UnicodeDecodeError):
            return []
        return chunk_text(content, CHUNK_SIZE_TOKENS) if content.strip() else []

    def delete_file(self, file_path: str):
        """Remove all vectors for a deleted file. Only removes state on success."""
        try:
            self._delete_file_vectors(file_path)
            self.state.remove(file_path)
            logger.info(f"Removed vectors for: {file_path}")
        except Exception as e:
            logger.error(f"Failed to delete vectors for {file_path}: {e} — state retained for retry")

    def _delete_file_vectors(self, file_path: str):
        """Delete all vectors associated with a file path. Raises on failure."""
        if file_path.startswith(WIKI_PATH):
            relative_path = "wiki/" + os.path.relpath(file_path, WIKI_PATH)
        elif file_path.startswith(DOCUMENTS_PATH):
            relative_path = "documents/" + os.path.relpath(file_path, DOCUMENTS_PATH)
        else:
            relative_path = os.path.basename(file_path)

        try:
            self.qdrant.delete(
                collection_name=COLLECTION_NAME,
                points_selector=Filter(
                    must=[
                        FieldCondition(
                            key="source_file",
                            match=MatchValue(value=relative_path),
                        )
                    ]
                ),
            )
        except Exception as e:
            logger.error(f"Delete vectors failed for {relative_path}: {e}")
            raise

    @staticmethod
    def _generate_point_id(file_path: str, chunk_index: int) -> int:
        """Generate a deterministic point ID from file path + chunk index."""
        key = f"{file_path}:{chunk_index}"
        h = hashlib.sha256(key.encode()).hexdigest()
        return int(h[:16], 16) & 0x7FFFFFFFFFFFFFFF  # Positive 64-bit int

    async def full_scan(self):
        """Perform a full scan of all watched directories."""
        logger.info("Starting full scan...")
        indexed_paths = self.state.get_all_paths()
        current_paths = set()

        for base_path in [WIKI_PATH, DOCUMENTS_PATH]:
            if not os.path.isdir(base_path):
                continue
            for root, _, files in os.walk(base_path):
                for filename in files:
                    if filename.startswith("."):
                        continue
                    file_path = os.path.join(root, filename)
                    current_paths.add(file_path)
                    await self.index_file(file_path)

        # Remove vectors for deleted files
        deleted = indexed_paths - current_paths
        for path in deleted:
            self.delete_file(path)
            logger.info(f"Cleaned up deleted file: {path}")

        logger.info(f"Full scan complete. Files: {len(current_paths)}, Deleted: {len(deleted)}")

    async def close(self):
        await self.embedding_client.close()
        await self.unstructured_client.close()


class IndexerEventHandler(FileSystemEventHandler):
    """Watchdog event handler with debouncing."""

    def __init__(self, indexer: Indexer, loop: asyncio.AbstractEventLoop, debounce: int):
        super().__init__()
        self.indexer = indexer
        self.loop = loop
        self.debounce = debounce
        self._pending: dict[str, float] = {}

    def on_created(self, event: FileSystemEvent):
        if not event.is_directory:
            self._schedule(event.src_path)

    def on_modified(self, event: FileSystemEvent):
        if not event.is_directory:
            self._schedule(event.src_path)

    def on_deleted(self, event: FileSystemEvent):
        if not event.is_directory:
            self.loop.call_soon_threadsafe(
                asyncio.ensure_future,
                self._handle_delete(event.src_path),
            )

    def _schedule(self, path: str):
        """Schedule indexing with debounce."""
        if os.path.basename(path).startswith("."):
            return
        self._pending[path] = time.time() + self.debounce
        self.loop.call_soon_threadsafe(
            asyncio.ensure_future,
            self._process_pending(),
        )

    async def _process_pending(self):
        """Process pending files after debounce period."""
        await asyncio.sleep(self.debounce + 1)
        now = time.time()
        ready = [p for p, t in list(self._pending.items()) if t <= now]
        for path in ready:
            self._pending.pop(path, None)
            await self.indexer.index_file(path)

    async def _handle_delete(self, path: str):
        self.indexer.delete_file(path)


async def main():
    """Main entry point for the indexer service."""
    logger.info("LeanKernel Knowledge Indexer starting...")
    logger.info(f"  Wiki path: {WIKI_PATH}")
    logger.info(f"  Documents path: {DOCUMENTS_PATH}")
    logger.info(f"  Qdrant: {QDRANT_HOST}:{QDRANT_PORT}/{COLLECTION_NAME}")
    logger.info(f"  Embedding model: {EMBEDDING_MODEL}")
    logger.info(f"  Rescan interval: {RESCAN_INTERVAL}s")

    # Wait for dependencies to be ready
    await wait_for_dependencies()

    indexer = Indexer()

    # Initial full scan
    await indexer.full_scan()

    # Set up file watchers
    loop = asyncio.get_event_loop()
    observer = Observer()

    for watch_path, debounce in [(WIKI_PATH, WIKI_DEBOUNCE), (DOCUMENTS_PATH, 30)]:
        if os.path.isdir(watch_path):
            handler = IndexerEventHandler(indexer, loop, debounce)
            observer.schedule(handler, watch_path, recursive=True)
            logger.info(f"Watching: {watch_path} (debounce: {debounce}s)")

    observer.start()

    # Periodic rescan loop
    try:
        while True:
            await asyncio.sleep(RESCAN_INTERVAL)
            await indexer.full_scan()
    except asyncio.CancelledError:
        pass
    finally:
        observer.stop()
        observer.join()
        await indexer.close()
        logger.info("Indexer shut down.")


async def wait_for_dependencies():
    """Wait for Qdrant and LiteLLM to become available."""
    max_retries = 30
    for i in range(max_retries):
        try:
            async with httpx.AsyncClient(timeout=5.0) as client:
                # Check Qdrant
                resp = await client.get(f"http://{QDRANT_HOST}:{QDRANT_PORT - 1}")
                # Check LiteLLM
                resp = await client.get(f"{LITELLM_URL}/health")
            logger.info("Dependencies ready.")
            return
        except Exception:
            if i < max_retries - 1:
                logger.info(f"Waiting for dependencies... ({i + 1}/{max_retries})")
                await asyncio.sleep(5)
            else:
                logger.warning("Dependencies may not be fully ready, proceeding anyway.")


if __name__ == "__main__":
    asyncio.run(main())

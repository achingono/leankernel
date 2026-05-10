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
import random
import re
import sqlite3
import time
from pathlib import Path
from typing import Any, Optional

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
DOCUMENTS_PATHS = [
    path.strip()
    for path in os.getenv("DOCUMENTS_PATHS", DOCUMENTS_PATH).split(",")
    if path.strip()
]
DATA_ROOT_PATH = os.getenv("DATA_ROOT_PATH", "/app/data")
EMBEDDING_MODEL = os.getenv("EMBEDDING_MODEL", "embedding-small")
EMBEDDING_DIMENSION = int(os.getenv("EMBEDDING_DIMENSION", "3072"))
EMBEDDING_REQUEST_DIMENSION = int(os.getenv("EMBEDDING_REQUEST_DIMENSION", str(EMBEDDING_DIMENSION)))
RESCAN_INTERVAL = int(os.getenv("RESCAN_INTERVAL_SECONDS", "300"))
WIKI_DEBOUNCE = int(os.getenv("WIKI_DEBOUNCE_SECONDS", "5"))
CHUNK_SIZE_TOKENS = int(os.getenv("CHUNK_SIZE_TOKENS", "500"))
STATE_DB_PATH = os.getenv("STATE_DB_PATH", "/app/data/indexer-state.db")
TAG_RULES_JSON = os.getenv("TAG_RULES_JSON", "[]")
EMBEDDING_MAX_RETRIES = int(os.getenv("EMBEDDING_MAX_RETRIES", "6"))
EMBEDDING_RETRY_BASE_SECONDS = float(os.getenv("EMBEDDING_RETRY_BASE_SECONDS", "0.75"))
EMBEDDING_RETRY_MAX_SECONDS = float(os.getenv("EMBEDDING_RETRY_MAX_SECONDS", "12"))
RETRYABLE_EMBEDDING_STATUSES = {408, 409, 425, 429, 500, 502, 503, 504}
RETRYABLE_EMBEDDING_ERRORS = (
    httpx.ConnectError,
    httpx.ReadTimeout,
    httpx.WriteTimeout,
    httpx.PoolTimeout,
    httpx.NetworkError,
)
UNSTRUCTURED_MAX_RETRIES = int(os.getenv("UNSTRUCTURED_MAX_RETRIES", "5"))
UNSTRUCTURED_RETRY_BASE_SECONDS = float(os.getenv("UNSTRUCTURED_RETRY_BASE_SECONDS", "1.0"))
UNSTRUCTURED_RETRY_MAX_SECONDS = float(os.getenv("UNSTRUCTURED_RETRY_MAX_SECONDS", "15"))
UNSTRUCTURED_STRATEGY = os.getenv("UNSTRUCTURED_STRATEGY", "fast")
UNSTRUCTURED_CHUNKING_STRATEGY = os.getenv("UNSTRUCTURED_CHUNKING_STRATEGY", "by_title")
UNSTRUCTURED_PDF_INFER_TABLE_STRUCTURE = os.getenv(
    "UNSTRUCTURED_PDF_INFER_TABLE_STRUCTURE",
    "false",
).lower() in {"1", "true", "yes", "on"}
MARKITDOWN_FALLBACK_ENABLED = os.getenv("MARKITDOWN_FALLBACK_ENABLED", "true").lower() in {
    "1",
    "true",
    "yes",
    "on",
}
MARKITDOWN_FALLBACK_MIN_BYTES = int(os.getenv("MARKITDOWN_FALLBACK_MIN_BYTES", "750000"))
MARKITDOWN_FALLBACK_EXTENSIONS = {
    ext.strip().lower()
    for ext in os.getenv("MARKITDOWN_FALLBACK_EXTENSIONS", ".pdf,.epub").split(",")
    if ext.strip()
}
RETRYABLE_UNSTRUCTURED_STATUSES = {408, 409, 425, 429, 500, 502, 503, 504}
RETRYABLE_UNSTRUCTURED_ERRORS = (
    httpx.ConnectError,
    httpx.ReadTimeout,
    httpx.WriteTimeout,
    httpx.PoolTimeout,
    httpx.NetworkError,
    httpx.RemoteProtocolError,  # Unstructured container crash mid-request ("Server disconnected")
)

# Document extensions that should go through Unstructured
UNSTRUCTURED_EXTENSIONS = {
    ".pdf", ".docx", ".doc", ".pptx", ".ppt", ".xlsx", ".xls",
    ".html", ".htm", ".epub", ".rtf", ".odt", ".csv", ".tsv",
    ".eml", ".msg", ".rst", ".org",
    ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tiff", ".tif",
}

# Text-based extensions that can be read directly
TEXT_EXTENSIONS = {".md", ".txt", ".json", ".yaml", ".yml", ".xml", ".log"}


def _retry_delay(
    attempt: int,
    base_seconds: float,
    max_seconds: float,
    retry_after: str | None = None,
) -> float:
    if retry_after and retry_after.isdigit():
        delay = min(float(retry_after), max_seconds)
    else:
        delay = min(base_seconds * (2 ** (attempt - 1)), max_seconds)
    return delay + random.uniform(0.0, 0.2)


def should_ignore_path(file_path: str) -> bool:
    """Ignore hidden files/directories (dotfiles) such as .health-check."""
    parts = Path(file_path).parts
    return any(part.startswith(".") for part in parts if part not in (".", ".."))


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

    def __init__(self, base_url: str, api_key: str, model: str, request_dimension: int | None = None):
        self.base_url = base_url.rstrip("/")
        self.api_key = api_key
        self.model = model
        self.request_dimension = request_dimension
        self.client = httpx.AsyncClient(timeout=60.0)

    async def embed(self, text: str) -> list[float]:
        """Generate embedding for a single text."""
        last_error: Exception | None = None

        for attempt in range(1, EMBEDDING_MAX_RETRIES + 1):
            try:
                return await self._embed_once(text)
            except httpx.HTTPStatusError as e:
                last_error = e
                status = e.response.status_code
                if status not in RETRYABLE_EMBEDDING_STATUSES or attempt == EMBEDDING_MAX_RETRIES:
                    raise

                await self._sleep_after_http_error(e, attempt)
            except RETRYABLE_EMBEDDING_ERRORS as e:
                last_error = e
                if attempt == EMBEDDING_MAX_RETRIES:
                    raise
                await self._sleep_after_connection_error(e, attempt)

        if last_error is not None:
            raise last_error
        raise RuntimeError("Embedding failed without an explicit exception")

    async def _embed_once(self, text: str) -> list[float]:
        response = await self.client.post(
            f"{self.base_url}/embeddings",
            json=self._embedding_payload(text),
            headers={"Authorization": f"Bearer {self.api_key}"},
        )
        response.raise_for_status()
        embedding = response.json()["data"][0]["embedding"]
        return self._fit_requested_dimension(embedding)

    def _embedding_payload(self, text: str) -> dict[str, Any]:
        payload: dict[str, Any] = {"input": text, "model": self.model}
        if self.request_dimension and self.request_dimension > 0:
            # `dimensions` is OpenAI-compatible and works across our configured
            # providers. Sending `dimension` causes Gemini to reject requests.
            payload["dimensions"] = self.request_dimension
        return payload

    def _fit_requested_dimension(self, embedding: list[float]) -> list[float]:
        if not self.request_dimension or self.request_dimension <= 0:
            return embedding
        if len(embedding) > self.request_dimension:
            return embedding[: self.request_dimension]
        if len(embedding) < self.request_dimension:
            raise RuntimeError(
                f"Embedding length {len(embedding)} is smaller than requested "
                f"dimension {self.request_dimension} for model {self.model}"
            )
        return embedding

    async def _sleep_after_http_error(self, error: httpx.HTTPStatusError, attempt: int):
        delay = self._retry_delay(attempt, error.response.headers.get("retry-after"))
        logger.warning(
            "Embedding transient HTTP %s (attempt %s/%s); retrying in %.2fs",
            error.response.status_code,
            attempt,
            EMBEDDING_MAX_RETRIES,
            delay,
        )
        await asyncio.sleep(delay)

    async def _sleep_after_connection_error(self, error: Exception, attempt: int):
        delay = self._retry_delay(attempt)
        logger.warning(
            "Embedding connection error (attempt %s/%s): %r; retrying in %.2fs",
            attempt,
            EMBEDDING_MAX_RETRIES,
            error,
            delay,
        )
        await asyncio.sleep(delay)

    @staticmethod
    def _retry_delay(attempt: int, retry_after: str | None = None) -> float:
        if retry_after and retry_after.isdigit():
            delay = min(float(retry_after), EMBEDDING_RETRY_MAX_SECONDS)
        else:
            delay = min(
                EMBEDDING_RETRY_BASE_SECONDS * (2 ** (attempt - 1)),
                EMBEDDING_RETRY_MAX_SECONDS,
            )
        return delay + random.uniform(0.0, 0.2)

    async def close(self):
        await self.client.aclose()


class UnstructuredClient:
    """Parses documents via Unstructured.io API."""

    def __init__(self, base_url: str):
        self.base_url = base_url.rstrip("/")
        self.client = httpx.AsyncClient(timeout=300.0)

    async def parse(self, file_path: str) -> list[dict]:
        """Parse a document file and return chunked elements."""
        last_error: Exception | None = None

        for attempt in range(1, UNSTRUCTURED_MAX_RETRIES + 1):
            try:
                request_data: dict[str, Any] = {
                    "strategy": UNSTRUCTURED_STRATEGY,
                    "chunking_strategy": UNSTRUCTURED_CHUNKING_STRATEGY,
                    "max_characters": CHUNK_SIZE_TOKENS * 4,
                }
                if UNSTRUCTURED_PDF_INFER_TABLE_STRUCTURE:
                    request_data["pdf_infer_table_structure"] = True

                with open(file_path, "rb") as f:
                    response = await self.client.post(
                        f"{self.base_url}/general/v0/general",
                        files={"files": (os.path.basename(file_path), f)},
                        data=request_data,
                    )
                response.raise_for_status()
                return response.json()
            except httpx.HTTPStatusError as e:
                last_error = e
                status = e.response.status_code
                if status not in RETRYABLE_UNSTRUCTURED_STATUSES or attempt == UNSTRUCTURED_MAX_RETRIES:
                    raise

                delay = _retry_delay(
                    attempt,
                    base_seconds=UNSTRUCTURED_RETRY_BASE_SECONDS,
                    max_seconds=UNSTRUCTURED_RETRY_MAX_SECONDS,
                    retry_after=e.response.headers.get("retry-after"),
                )
                logger.warning(
                    "Unstructured HTTP %s for %s (attempt %s/%s); retrying in %.2fs",
                    status,
                    file_path,
                    attempt,
                    UNSTRUCTURED_MAX_RETRIES,
                    delay,
                )
                await asyncio.sleep(delay)
            except RETRYABLE_UNSTRUCTURED_ERRORS as e:
                last_error = e
                if attempt == UNSTRUCTURED_MAX_RETRIES:
                    raise

                delay = _retry_delay(
                    attempt,
                    base_seconds=UNSTRUCTURED_RETRY_BASE_SECONDS,
                    max_seconds=UNSTRUCTURED_RETRY_MAX_SECONDS,
                )
                logger.warning(
                    "Unstructured connection error for %s (attempt %s/%s): %r; retrying in %.2fs",
                    file_path,
                    attempt,
                    UNSTRUCTURED_MAX_RETRIES,
                    e,
                    delay,
                )
                await asyncio.sleep(delay)

        if last_error is not None:
            raise last_error
        raise RuntimeError("Unstructured parse failed without an explicit exception")

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

    end_idx = find_frontmatter_end(lines)
    if end_idx is None:
        return None

    frontmatter = parse_frontmatter_lines(lines[1:end_idx])
    body_lines = lines[end_idx + 1:]
    body = "\n".join(body_lines).strip()
    facts = extract_wiki_facts(body_lines)

    return {
        "id": frontmatter.get("id", ""),
        "dimension": frontmatter.get("dimension", ""),
        "subject": frontmatter.get("subject", ""),
        "body": body,
        "facts": facts,
        "text_for_embedding": text_for_wiki_embedding(frontmatter, facts, body),
    }


def find_frontmatter_end(lines: list[str]) -> int | None:
    for i in range(1, len(lines)):
        if lines[i].strip() == "---":
            return i
    return None


def parse_frontmatter_lines(lines: list[str]) -> dict[str, str]:
    frontmatter = {}
    for line in lines:
        colon_idx = line.find(":")
        if colon_idx > 0:
            key = line[:colon_idx].strip()
            value = line[colon_idx + 1:].strip()
            frontmatter[key] = value
    return frontmatter


def extract_wiki_facts(body_lines: list[str]) -> list[str]:
    facts = []
    for line in body_lines:
        line = line.strip()
        if line.startswith("- ") and not line.startswith("- ["):
            claim = re.sub(r"\s*<!--\{[^}]*\}-->$", "", line[2:])
            if claim:
                facts.append(claim)
    return facts


def text_for_wiki_embedding(frontmatter: dict[str, str], facts: list[str], body: str) -> str:
    if not facts:
        return body
    return f"{frontmatter.get('dimension', '')}:{frontmatter.get('subject', '')} — " + "; ".join(facts)


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


def convert_with_markitdown(file_path: str) -> str:
    """Convert a file to text via MarkItDown."""
    from markitdown import MarkItDown

    converter = MarkItDown()
    result = converter.convert(file_path)

    text = getattr(result, "text_content", None)
    if not text:
        text = getattr(result, "markdown", None)
    if not text:
        text = str(result or "")
    return text or ""


def source_metadata(file_path: str) -> tuple[str, str]:
    """Return source_file payload path and source_type for a file."""
    if _is_subpath(file_path, WIKI_PATH):
        return _relative_source_path(file_path, WIKI_PATH, "wiki", "wiki"), "wiki"
    for documents_path in DOCUMENTS_PATHS:
        if _is_subpath(file_path, documents_path):
            return _relative_source_path(file_path, documents_path, "documents", "document"), "document"
    return os.path.basename(file_path), "document"


def _is_subpath(file_path: str, base_path: str) -> bool:
    """Return True when file_path is inside base_path."""
    try:
        file_abs = os.path.abspath(file_path)
        base_abs = os.path.abspath(base_path)
        return os.path.commonpath([file_abs, base_abs]) == base_abs
    except ValueError:
        return False


def _relative_source_path(file_path: str, base_path: str, fallback_prefix: str, source_type: str) -> str:
    """Build a stable source_file path, preferring relative paths under DATA_ROOT_PATH."""
    if _is_subpath(file_path, DATA_ROOT_PATH):
        relative_to_data = os.path.relpath(file_path, DATA_ROOT_PATH)
        if source_type == "wiki" and not relative_to_data.startswith("wiki/"):
            return "wiki/" + os.path.relpath(file_path, base_path)
        return relative_to_data
    return f"{fallback_prefix}/" + os.path.relpath(file_path, base_path)


def chunk_payload(
    chunk_text_content: str,
    chunk_index: int,
    source_type: str,
    relative_path: str,
    tags: list[str],
    wiki_data: dict | None,
) -> dict[str, Any]:
    payload = {
        "source_type": source_type,
        "source_file": relative_path,
        "chunk_index": chunk_index,
        "text": chunk_text_content,
        "tags": tags,
        "indexed_at": int(time.time()),
    }
    if source_type == "wiki" and wiki_data:
        payload["entry_id"] = wiki_data["id"]
        payload["dimension"] = wiki_data["dimension"]
        payload["subject"] = wiki_data["subject"]
    return payload


class Indexer:
    """Main indexing engine."""

    def __init__(self):
        self.state = StateDB(STATE_DB_PATH)
        self.tag_resolver = TagResolver(TAG_RULES_JSON, default_tags=["general"])
        self.embedding_client = EmbeddingClient(
            LITELLM_URL,
            LITELLM_API_KEY,
            EMBEDDING_MODEL,
            request_dimension=EMBEDDING_REQUEST_DIMENSION,
        )
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
        if should_ignore_path(file_path):
            return

        if not os.path.isfile(file_path):
            return

        try:
            file_hash = compute_file_hash(file_path)
            stored_hash = self.state.get_file_hash(file_path)
        except Exception as e:
            logger.error(f"Failed to read/hash {file_path}: {e}")
            return

        if stored_hash == file_hash:
            logger.debug(f"Skipping unchanged file: {file_path}")
            return

        relative_path, source_type = source_metadata(file_path)
        tags = self.tag_resolver.resolve(relative_path)
        ext = os.path.splitext(file_path)[1].lower()

        try:
            chunks = await self._process_file_by_type(file_path, source_type, ext)
            if chunks is None:
                return
            if not chunks:
                logger.warning(f"No content extracted from: {file_path}")
                return

            self._delete_existing_vectors_for_reindex(file_path)
            expected_chunks = len(chunks)
            points, failed_chunks = await self._build_points(
                file_path,
                chunks,
                source_type,
                relative_path,
                tags,
            )

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

    async def _process_file_by_type(self, file_path: str, source_type: str, ext: str) -> list[str] | None:
        if source_type == "wiki" and ext == ".md":
            return await self._process_wiki_file(file_path)
        if ext in UNSTRUCTURED_EXTENSIONS:
            return await self._process_unstructured_file(file_path)
        if ext in TEXT_EXTENSIONS:
            return await self._process_text_file(file_path)

        logger.warning(f"Unsupported file type: {file_path}")
        return None

    def _delete_existing_vectors_for_reindex(self, file_path: str):
        try:
            self._delete_file_vectors(file_path)
        except Exception as e:
            logger.debug(f"Pre-index delete skipped for {file_path}: {e}")

    async def _build_points(
        self,
        file_path: str,
        chunks: list[str],
        source_type: str,
        relative_path: str,
        tags: list[str],
    ) -> tuple[list[PointStruct], int]:
        points = []
        failed_chunks = 0
        wiki_data = parse_wiki_markdown(file_path) if source_type == "wiki" else None

        for i, chunk_text_content in enumerate(chunks):
            point = await self._build_point(
                file_path,
                i,
                chunk_text_content,
                source_type,
                relative_path,
                tags,
                wiki_data,
            )
            if point:
                points.append(point)
            else:
                failed_chunks += 1

        return points, failed_chunks

    async def _build_point(
        self,
        file_path: str,
        chunk_index: int,
        chunk_text_content: str,
        source_type: str,
        relative_path: str,
        tags: list[str],
        wiki_data: dict | None,
    ) -> PointStruct | None:
        try:
            embedding = await self.embedding_client.embed(chunk_text_content)
        except Exception as e:
            logger.error(f"Embedding failed for chunk {chunk_index} of {file_path}: {e!r}")
            return None

        payload = chunk_payload(
            chunk_text_content,
            chunk_index,
            source_type,
            relative_path,
            tags,
            wiki_data,
        )
        return PointStruct(
            id=self._generate_point_id(file_path, chunk_index),
            vector=embedding,
            payload=payload,
        )

    async def _process_wiki_file(self, file_path: str) -> list[str]:
        """Process a wiki markdown file into text chunks."""
        wiki_data = parse_wiki_markdown(file_path)
        if not wiki_data:
            return []
        text = wiki_data["text_for_embedding"]
        return chunk_text(text, CHUNK_SIZE_TOKENS) if text else []

    async def _process_unstructured_file(self, file_path: str) -> list[str]:
        """Process a document through Unstructured API."""
        try:
            elements = await self.unstructured_client.parse(file_path)
            chunks = []
            for element in elements:
                text = element.get("text", "").strip()
                if text and len(text) > 20:
                    chunks.append(text)
            return chunks
        except Exception as unstructured_error:
            ext = os.path.splitext(file_path)[1].lower()
            if not self._should_try_markitdown_fallback(file_path, ext):
                raise

            logger.warning(
                "Unstructured failed after retries for %s (%r). Trying MarkItDown fallback.",
                file_path,
                unstructured_error,
            )
            fallback_text = await asyncio.to_thread(convert_with_markitdown, file_path)
            fallback_chunks = chunk_text(fallback_text, CHUNK_SIZE_TOKENS) if fallback_text.strip() else []

            if fallback_chunks:
                logger.info(
                    "MarkItDown fallback succeeded for %s with %s chunks",
                    file_path,
                    len(fallback_chunks),
                )
                return fallback_chunks

            logger.warning("MarkItDown fallback produced no content for: %s", file_path)
            raise

    @staticmethod
    def _should_try_markitdown_fallback(file_path: str, ext: str) -> bool:
        if not MARKITDOWN_FALLBACK_ENABLED:
            return False
        if ext not in MARKITDOWN_FALLBACK_EXTENSIONS:
            return False
        try:
            return os.path.getsize(file_path) >= MARKITDOWN_FALLBACK_MIN_BYTES
        except OSError:
            return False

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
        if should_ignore_path(file_path):
            return

        try:
            self._delete_file_vectors(file_path)
            self.state.remove(file_path)
            logger.info(f"Removed vectors for: {file_path}")
        except Exception as e:
            logger.error(f"Failed to delete vectors for {file_path}: {e} — state retained for retry")

    def _delete_file_vectors(self, file_path: str):
        """Delete all vectors associated with a file path. Raises on failure."""
        relative_path, _ = source_metadata(file_path)

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

        def _walk_error(error: OSError):
            logger.error(f"Directory walk error: {error}")

        scan_paths = [WIKI_PATH, *DOCUMENTS_PATHS]
        for base_path in scan_paths:
            if not os.path.isdir(base_path):
                continue
            for root, _, files in os.walk(base_path, onerror=_walk_error):
                for filename in files:
                    file_path = os.path.join(root, filename)
                    if should_ignore_path(file_path):
                        continue
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
        self.on_created(event)

    def on_deleted(self, event: FileSystemEvent):
        if not event.is_directory:
            if should_ignore_path(event.src_path):
                return
            self.loop.call_soon_threadsafe(
                asyncio.ensure_future,
                self._handle_delete(event.src_path),
            )

    def _schedule(self, path: str):
        """Schedule indexing with debounce."""
        if should_ignore_path(path):
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
    logger.info(f"  Documents paths: {DOCUMENTS_PATHS}")
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

    watch_targets = [(WIKI_PATH, WIKI_DEBOUNCE)] + [(path, 30) for path in DOCUMENTS_PATHS]
    for watch_path, debounce in watch_targets:
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
    """Wait for Qdrant, LiteLLM, and Unstructured to become available."""
    max_retries = 30
    for i in range(max_retries):
        try:
            async with httpx.AsyncClient(timeout=5.0) as client:
                # Check Qdrant
                resp = await client.get(f"http://{QDRANT_HOST}:{QDRANT_PORT - 1}")
                resp.raise_for_status()
                # Check LiteLLM
                resp = await client.get(f"{LITELLM_URL}/health/liveliness")
                resp.raise_for_status()
                # Check Unstructured
                resp = await client.get(f"{UNSTRUCTURED_API_URL}/healthcheck")
                resp.raise_for_status()
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

-- Restores the remote data dump into the live gbrain DB by loading each table into a temp
-- table and re-inserting with an id offset (+1000000) so remote ids never collide with
-- pre-existing local rows. Dependents (chunks/versions/tags/aliases) are offset consistently.
-- Requires the per-table data files at /tmp/gbrain-import-data/ (pg_dump COPY text format).
--
-- The temp tables mirror the REMOTE dump column order (the remote pages table orders its
-- trailing columns differently from the local v125 schema), and the final INSERTs map by
-- column NAME into the local tables, so order drift between environments is harmless.
BEGIN;

DELETE FROM content_chunks WHERE id >= 1000000 OR page_id >= 1000000;
DELETE FROM page_versions WHERE id >= 1000000 OR page_id >= 1000000;
DELETE FROM tags WHERE id >= 1000000 OR page_id >= 1000000;
DELETE FROM page_aliases WHERE id >= 1000000;
DELETE FROM pages WHERE id >= 1000000;

CREATE TEMP TABLE imp_pages (
  id integer, source_id text, slug text, type text, page_kind text, title text,
  compiled_truth text, timeline text, frontmatter jsonb, content_hash text,
  emotional_weight real, created_at timestamptz, updated_at timestamptz, deleted_at timestamptz,
  effective_date timestamptz, effective_date_source text, import_filename text,
  salience_touched_at timestamptz, last_retrieved_at timestamptz, links_extracted_at timestamptz,
  contextual_retrieval_mode text, corpus_generation text, generation bigint, search_vector tsvector,
  ingested_via text, ingested_at timestamptz, source_uri text, source_kind text,
  embedding_signature text, emotional_weight_recomputed_at timestamptz, chunker_version smallint,
  source_path text
) ON COMMIT DROP;

CREATE TEMP TABLE imp_chunks (LIKE content_chunks INCLUDING ALL) ON COMMIT DROP;
CREATE TEMP TABLE imp_versions (LIKE page_versions INCLUDING ALL) ON COMMIT DROP;
CREATE TEMP TABLE imp_tags (LIKE tags INCLUDING ALL) ON COMMIT DROP;
CREATE TEMP TABLE imp_aliases (LIKE page_aliases INCLUDING ALL) ON COMMIT DROP;

\copy imp_pages FROM '/tmp/gbrain-import-data/pages.txt'
\copy imp_chunks FROM '/tmp/gbrain-import-data/content_chunks.txt'
\copy imp_versions FROM '/tmp/gbrain-import-data/page_versions.txt'
\copy imp_tags FROM '/tmp/gbrain-import-data/tags.txt'
\copy imp_aliases FROM '/tmp/gbrain-import-data/page_aliases.txt'

INSERT INTO pages (id, source_id, slug, type, page_kind, title, compiled_truth, timeline, frontmatter, content_hash, emotional_weight, created_at, updated_at, deleted_at, effective_date, effective_date_source, import_filename, salience_touched_at, last_retrieved_at, links_extracted_at, contextual_retrieval_mode, corpus_generation, generation, search_vector, ingested_via, ingested_at, source_uri, source_kind, embedding_signature, emotional_weight_recomputed_at, chunker_version, source_path)
SELECT id + 1000000, source_id, slug, type, page_kind, title, compiled_truth, timeline, frontmatter, content_hash, emotional_weight, created_at, updated_at, deleted_at, effective_date, effective_date_source, import_filename, salience_touched_at, last_retrieved_at, links_extracted_at, contextual_retrieval_mode, corpus_generation, generation, search_vector, ingested_via, ingested_at, source_uri, source_kind, embedding_signature, emotional_weight_recomputed_at, chunker_version, source_path
FROM imp_pages;

INSERT INTO content_chunks (id, page_id, chunk_index, chunk_text, chunk_source, embedding, model, token_count, embedded_at, created_at, language, symbol_name, symbol_type, start_line, end_line, parent_symbol_path, doc_comment, symbol_name_qualified, search_vector, modality, embedding_image, embedding_multimodal, edges_backfilled_at)
SELECT id + 1000000, page_id + 1000000, chunk_index, chunk_text, chunk_source, embedding, model, token_count, embedded_at, created_at, language, symbol_name, symbol_type, start_line, end_line, parent_symbol_path, doc_comment, symbol_name_qualified, search_vector, modality, embedding_image, embedding_multimodal, edges_backfilled_at
FROM imp_chunks;

INSERT INTO page_versions (id, page_id, compiled_truth, frontmatter, snapshot_at)
SELECT id + 1000000, page_id + 1000000, compiled_truth, frontmatter, snapshot_at
FROM imp_versions;

INSERT INTO tags (id, page_id, tag)
SELECT id + 1000000, page_id + 1000000, tag
FROM imp_tags;

INSERT INTO page_aliases (id, source_id, alias_norm, slug, created_at)
SELECT id + 1000000, source_id, alias_norm, slug, created_at
FROM imp_aliases;

COMMIT;

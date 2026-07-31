-- Applies the import plan. Loads the manifest CSV (always at the fixed container path
-- /tmp/gbrain-import-manifest.csv) and:
--   1. rewrites page slugs per action (keep/rewrite/merged_winner)
--   2. repoints dependents (chunks, versions, tags) of merged losers onto the winner page
--   3. deletes merged loser pages
--   4. rewrites page_aliases targets through the mapping (losers resolve to the winner's canonical slug)
--   5. drops imported embeddings (R5) so the local re-embed step regenerates them
-- Single transaction; fails atomically on any conflict (e.g. pre-existing slug collision).
BEGIN;

CREATE TEMP TABLE slug_map (
  old_slug text PRIMARY KEY,
  new_slug text NOT NULL,
  type text,
  channel_copy text,
  collision_strategy text NOT NULL,
  action text NOT NULL,
  updated_at timestamptz
);
\copy slug_map FROM '/tmp/gbrain-import-manifest.csv' WITH (FORMAT csv, HEADER true)

DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM slug_map WHERE new_slug IS NULL OR new_slug = '') THEN
    RAISE EXCEPTION 'unclassified row: empty new_slug (NULL compiled_truth?)';
  END IF;
  IF EXISTS (
    SELECT 1 FROM slug_map m
    JOIN pages p ON p.slug = m.new_slug AND p.slug <> m.old_slug
  ) THEN
    RAISE EXCEPTION 'pre-existing slug collision detected: new_slug already exists on a different local page';
  END IF;
END $$;

UPDATE pages p SET slug = m.new_slug
FROM slug_map m
WHERE p.slug = m.old_slug AND m.action IN ('keep', 'rewrite', 'merged_winner');

CREATE TEMP TABLE merge_map AS
  SELECT s.old_slug AS loser_slug, s.new_slug AS winner_slug
  FROM slug_map s
  WHERE s.action = 'merged_loser';

-- repoint dependents onto the winner page before deleting losers; skip rows that
-- would violate unique (page_id, chunk_index) / (page_id, tag) (byte-identical content
-- produces identical chunks/tags, so the winner's copy is authoritative)
UPDATE content_chunks c SET page_id = w.id
FROM merge_map mm
JOIN pages l ON l.slug = mm.loser_slug
JOIN pages w ON w.slug = mm.winner_slug
WHERE c.page_id = l.id
  AND NOT EXISTS (SELECT 1 FROM content_chunks c2 WHERE c2.page_id = w.id AND c2.chunk_index = c.chunk_index);

UPDATE page_versions v SET page_id = w.id
FROM merge_map mm
JOIN pages l ON l.slug = mm.loser_slug
JOIN pages w ON w.slug = mm.winner_slug
WHERE v.page_id = l.id;

UPDATE tags t SET page_id = w.id
FROM merge_map mm
JOIN pages l ON l.slug = mm.loser_slug
JOIN pages w ON w.slug = mm.winner_slug
WHERE t.page_id = l.id
  AND NOT EXISTS (SELECT 1 FROM tags t2 WHERE t2.page_id = w.id AND t2.tag = t.tag);

DELETE FROM pages p USING merge_map mm WHERE p.slug = mm.loser_slug;

-- aliases: rewrite targets through the mapping; losers resolve to the winner's canonical slug;
-- skip rows already satisfied (unique (source_id, alias_norm, slug))
UPDATE page_aliases a SET slug = m.new_slug
FROM slug_map m
WHERE a.slug = m.old_slug
  AND m.action IN ('rewrite', 'merged_winner', 'merged_loser')
  AND NOT EXISTS (
    SELECT 1 FROM page_aliases a2
    WHERE a2.source_id = a.source_id AND a2.alias_norm = a.alias_norm AND a2.slug = m.new_slug AND a2.id <> a.id
  );

-- R5: drop remote embeddings; local re-embed regenerates with openai:embedding.
-- model stays (NOT NULL); the re-embed step overwrites it for chunks it embeds.
UPDATE content_chunks
SET embedding = NULL, embedding_image = NULL, embedding_multimodal = NULL,
    search_vector = NULL, token_count = NULL, embedded_at = NULL
WHERE embedding IS NOT NULL OR embedding_image IS NOT NULL OR embedding_multimodal IS NOT NULL;

COMMIT;

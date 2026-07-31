-- Computes the slug rewrite plan for the imported remote pages.
-- Requires psql variables: LT (local tenant), LP (local person), LU (local user), CH_MEM (memory channel).
-- Emits CSV rows: old_slug,new_slug,type,channel_copy,collision_strategy,action,updated_at
-- Actions: keep (already canonical, no-op), rewrite (A/C rewrite), merged_winner (B duplicate kept),
--          merged_loser (B duplicate merged away, dependents repointed).
-- Collision strategy 'merged' marks byte-identical doc/% re-upload variants (newest updated_at wins).
WITH mapped AS (
  SELECT p.id, p.slug AS old_slug, p.type, p.updated_at,
    CASE
      WHEN p.slug LIKE 'learning/facts/%' THEN 'memory/' || :'LT' || '/' || :'LP' || '/' || :'CH_MEM' || '/' || p.slug
      WHEN p.slug LIKE 'doc/%' THEN 'documents/' || :'LT' || '/user/00000000-0000-0000-0000-000000000000/' || :'LU' || '/' || encode(digest(p.compiled_truth, 'sha256'), 'hex')
      WHEN p.slug LIKE 'memory/%' OR p.slug LIKE 'documents/%' OR p.slug = '__lk_probe_write__' THEN p.slug
      ELSE 'memory/' || :'LT' || '/' || :'LP' || '/' || :'CH_MEM' || '/' || p.slug
    END AS new_slug,
    CASE
      WHEN p.slug LIKE 'doc/%' THEN ''
      WHEN p.slug LIKE 'memory/%' OR p.slug LIKE 'documents/%' OR p.slug = '__lk_probe_write__' THEN ''
      ELSE :'CH_MEM'
    END AS channel_copy
  FROM pages p
),
b_fp AS (
  SELECT m.old_slug,
         count(*) OVER (PARTITION BY m.new_slug) AS cnt,
         row_number() OVER (PARTITION BY m.new_slug ORDER BY m.updated_at DESC, m.id ASC) AS rn
  FROM mapped m
  WHERE m.old_slug LIKE 'doc/%'
)
SELECT m.old_slug,
       m.new_slug,
       m.type,
       m.channel_copy,
       CASE WHEN b.cnt > 1 THEN 'merged' ELSE 'none' END AS collision_strategy,
       CASE
         WHEN m.new_slug = m.old_slug THEN 'keep'
         WHEN b.cnt > 1 AND b.rn = 1 THEN 'merged_winner'
         WHEN b.cnt > 1 THEN 'merged_loser'
         ELSE 'rewrite'
       END AS action,
       m.updated_at
FROM mapped m
LEFT JOIN b_fp b ON b.old_slug = m.old_slug AND m.old_slug LIKE 'doc/%'
ORDER BY m.old_slug;

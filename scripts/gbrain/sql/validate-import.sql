-- Post-import validation. Every metric must be in the expected range:
--   legacy learning/facts, legacy doc/%, legacy raw slugs  -> 0
--   alias orphans, tag orphans                            -> 0
--   embedding null                                        -> 0 (after re-embed)
--   embedding model                                       -> single openai:embedding
SELECT 'legacy learning/facts' AS metric, count(*) AS n FROM pages WHERE slug LIKE 'learning/facts/%';
SELECT 'legacy doc/%' AS metric, count(*) AS n FROM pages WHERE slug LIKE 'doc/%';
SELECT 'legacy raw slugs' AS metric, count(*) AS n
FROM pages
WHERE slug NOT LIKE 'memory/%' AND slug NOT LIKE 'documents/%' AND slug <> '__lk_probe_write__';
SELECT 'category' AS metric, split_part(slug, '/', 1) AS detail, count(*) AS n
FROM pages GROUP BY 2 ORDER BY 3 DESC;
SELECT 'alias orphans' AS metric, count(*) AS n
FROM page_aliases a LEFT JOIN pages p ON p.slug = a.slug WHERE p.id IS NULL;
SELECT 'tag orphans' AS metric, count(*) AS n
FROM tags t LEFT JOIN pages p ON p.id = t.page_id WHERE p.id IS NULL;
SELECT 'embedding null' AS metric, count(*) AS n FROM content_chunks WHERE embedding IS NULL;
SELECT 'embedding model' AS metric, COALESCE(model, '(null)') AS detail, count(*) AS n
FROM content_chunks WHERE embedding IS NOT NULL GROUP BY 2 ORDER BY 3 DESC;
SELECT 'documents user-scope' AS metric, count(*) AS n FROM pages WHERE slug LIKE 'documents/%/user/%';
SELECT 'fp mismatch' AS metric, count(*) AS n
FROM pages p
WHERE p.slug LIKE 'documents/%' AND split_part(p.slug, '/', 6) <> encode(digest(p.compiled_truth, 'sha256'), 'hex');
SELECT 'memory channels' AS metric, split_part(slug, '/', 4) AS detail, count(*) AS n
FROM pages WHERE slug LIKE 'memory/%' GROUP BY 2 ORDER BY 2;

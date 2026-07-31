-- Dry-run analysis of the import plan. Loads the manifest CSV (always at the fixed container
-- path /tmp/gbrain-import-manifest.csv) and reports category counts, collision groups, and fails
-- hard on pre-existing slug collisions or unclassified rows (empty new_slug).
CREATE TEMP TABLE manifest_plan (
  old_slug text,
  new_slug text,
  type text,
  channel_copy text,
  collision_strategy text,
  action text,
  updated_at timestamptz
);
\copy manifest_plan FROM '/tmp/gbrain-import-manifest.csv' WITH (FORMAT csv, HEADER true)

DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM manifest_plan m
    JOIN pages p ON p.slug = m.new_slug AND p.slug <> m.old_slug
  ) THEN
    RAISE EXCEPTION 'pre-existing slug collision detected: new_slug already exists on a different local page';
  END IF;
  IF EXISTS (SELECT 1 FROM manifest_plan WHERE new_slug IS NULL OR new_slug = '') THEN
    RAISE EXCEPTION 'unclassified row: empty new_slug (NULL compiled_truth?)';
  END IF;
END $$;

SELECT 'category' AS metric, split_part(new_slug, '/', 1) AS detail, count(*) AS n
FROM manifest_plan GROUP BY 2 ORDER BY 3 DESC;

SELECT 'action' AS metric, action AS detail, count(*) AS n
FROM manifest_plan GROUP BY 2 ORDER BY 3 DESC;

SELECT 'collision_group' AS metric, new_slug AS detail,
       count(*) AS n,
       string_agg(old_slug, ', ' ORDER BY updated_at DESC, old_slug) AS members
FROM manifest_plan
WHERE collision_strategy = 'merged'
GROUP BY new_slug
HAVING count(*) > 1
ORDER BY n DESC;

SELECT 'type' AS metric, type AS detail, count(*) AS n
FROM manifest_plan GROUP BY 2 ORDER BY 3 DESC;

SELECT 'channel_copy' AS metric, COALESCE(channel_copy, '(none)') AS detail, count(*) AS n
FROM manifest_plan GROUP BY 2 ORDER BY 3 DESC;

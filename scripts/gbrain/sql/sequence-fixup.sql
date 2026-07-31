-- Fixes serial sequences after restoring the remote data-only dump (remote ids preserved).
SELECT setval(pg_get_serial_sequence('pages', 'id'), (SELECT max(id) FROM pages), true);
SELECT setval(pg_get_serial_sequence('content_chunks', 'id'), (SELECT max(id) FROM content_chunks), true);
SELECT setval(pg_get_serial_sequence('page_versions', 'id'), (SELECT max(id) FROM page_versions), true);
SELECT setval(pg_get_serial_sequence('tags', 'id'), (SELECT max(id) FROM tags), true);
SELECT setval(pg_get_serial_sequence('page_aliases', 'id'), (SELECT max(id) FROM page_aliases), true);

-- LeanKernel Database Initialization
-- Ensures pgvector extension is available

CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- GBrain manages its own local brain database state.
-- LeanKernel ensures its PostgreSQL tables exist during gateway startup.

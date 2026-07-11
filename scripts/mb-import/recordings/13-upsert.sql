-- Upsert the distilled tracklists and version edges into the LIVE Grimoire database.
-- Runs against grimoire-postgres-dev. The staging tables in schema mb_import are loaded (by
-- run.sh) from the TSVs the distillation produced. This script does the set-based upsert.
--
-- ADDITIVE ONLY: this pass writes the new `recordings` and `cover_versions` tables and touches
-- nothing else — no artist/release/edge/label row and no enrichment column is modified
-- (brief / D5). Idempotent: recordings upsert by (release_id, position); cover edges by the
-- (original, cover) pair; re-running yields identical rows.

\set ON_ERROR_STOP on
BEGIN;

-- Recordings: resolve release_mbid (a release-group gid) to the Grimoire releases row.
-- Idempotent by (release_id, position) — position is 1-based and unique within a release.
INSERT INTO recordings (id, mbid, release_id, title, length_ms, position)
SELECT gen_random_uuid(), s.recording_mbid, rel.id, s.title, s.length_ms, s.position
FROM mb_import.stage_recordings s
JOIN releases rel ON rel.mbid = s.release_mbid
ON CONFLICT (release_id, position) DO UPDATE SET
    mbid      = EXCLUDED.mbid,
    title     = EXCLUDED.title,
    length_ms = EXCLUDED.length_ms;

-- A recording MBID is not unique in `recordings` (the same recording can be a track on several
-- releases), so resolve each cover endpoint to ONE deterministic row. PostgreSQL has no
-- min(uuid) aggregate, so DISTINCT ON with ORDER BY id picks the lowest-id row per mbid.
DROP TABLE IF EXISTS mb_import.rec_by_mbid;
CREATE TABLE mb_import.rec_by_mbid AS
SELECT DISTINCT ON (mbid) mbid, id FROM recordings ORDER BY mbid, id;
CREATE INDEX ON mb_import.rec_by_mbid (mbid);

-- Cover/version edges: both endpoints resolved to recordings rows; idempotent by the
-- (original, cover) pair. Several staged mbid-pairs (or several relations for one pair) can
-- resolve to the SAME (original_id, cover_id) — a single INSERT..ON CONFLICT may not touch a
-- row twice, so DISTINCT ON collapses them to one edge (keeping the first relation) first.
INSERT INTO cover_versions (id, original_recording_id, cover_recording_id, relation)
SELECT gen_random_uuid(), d.oid, d.cid, d.relation
FROM (
    SELECT DISTINCT ON (o.id, c.id)
           o.id AS oid, c.id AS cid, s.relation
    FROM mb_import.stage_covers s
    JOIN mb_import.rec_by_mbid o ON o.mbid = s.original_mbid
    JOIN mb_import.rec_by_mbid c ON c.mbid = s.cover_mbid
    WHERE o.id <> c.id
    ORDER BY o.id, c.id, s.relation
) d
ON CONFLICT (original_recording_id, cover_recording_id) DO UPDATE SET
    relation = EXCLUDED.relation;

COMMIT;

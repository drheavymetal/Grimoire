-- Distil the tracklists (C7 duration, C21 titles) and the version graph (C10) for OUR releases
-- from the temporary MB Postgres into staging tables shaped like Grimoire. run.sh COPYs these
-- out and upserts them into the live Grimoire DB (13-upsert.sql).
--
-- Our releases.mbid is a release-GROUP gid. A group has several releases (pressings); we pick
-- ONE representative release per group — the one with the most complete tracklist (max summed
-- medium.track_count, tie-break lowest release id) — and take ITS tracks. This is deterministic
-- and gives the fullest track list without inventing anything.
--
-- Covers: MusicBrainz has no atomic recording->recording "cover" link — cover attribution
-- proper lives at the work level (the reserved classical model, D11). What MB exposes at the
-- recording level is the "covers and versions" family of l_recording_recording relations, so
-- that family (other versions / edit / remaster / a cappella / instrumental / karaoke / remix)
-- is the honest v1 signal for C10. Each edge keeps its MB relation name.
SET synchronous_commit = off;
SET work_mem = '512MB';
-- A container's default /dev/shm is small; serial execution avoids parallel-hash-join /dev/shm
-- overflow on the big joins (run.sh also passes --shm-size=1g; this makes it safe either way).
SET max_parallel_workers_per_gather = 0;

-- our_rg(gid) = our releases.mbid, loaded by run.sh BEFORE this script; only guard existence.
CREATE TABLE IF NOT EXISTS our_rg (gid uuid);

-- 1. Map our release-group gids to MB release_group ids.
DROP TABLE IF EXISTS corpus_rg;
CREATE TABLE corpus_rg AS
SELECT rg.id AS rg_id, rg.gid
FROM release_group rg
JOIN our_rg o ON o.gid = rg.gid;
CREATE INDEX ON corpus_rg (rg_id);
CREATE INDEX ON corpus_rg (gid);

-- 2. Candidate releases of those groups, with total track count (across their media).
DROP TABLE IF EXISTS rel_tc;
CREATE TABLE rel_tc AS
SELECT r.id AS release_id, r.release_group AS rg_id,
       COALESCE(SUM(m.track_count), 0) AS tracks
FROM release r
JOIN corpus_rg c ON c.rg_id = r.release_group
LEFT JOIN medium m ON m.release = r.id
GROUP BY r.id, r.release_group;

-- 3. Representative release per group: most complete tracklist, tie-break lowest release id.
DROP TABLE IF EXISTS rep_release;
CREATE TABLE rep_release AS
SELECT DISTINCT ON (rg_id) rg_id, release_id
FROM rel_tc
ORDER BY rg_id, tracks DESC, release_id;
CREATE INDEX ON rep_release (release_id);
CREATE INDEX ON rep_release (rg_id);

-- 4. stage_recordings: one row per (non-data) track of the representative release.
--    title  = release-specific track name, blank -> recording name (never null: WHERE guards).
--    length = track length in ms, absent -> recording length (both may be null; C7 stays honest).
--    position = 1-based across all media of the release (disc1..discN), so unique per release.
DROP TABLE IF EXISTS stage_recordings;
CREATE TABLE stage_recordings AS
SELECT
  rg.gid AS release_mbid,
  rec.gid AS recording_mbid,
  COALESCE(NULLIF(btrim(t.name), ''), rec.name) AS title,
  COALESCE(t.length, rec.length) AS length_ms,
  (row_number() OVER (PARTITION BY rep.release_id ORDER BY m.position, t.position, t.id))::int AS position
FROM rep_release rep
JOIN corpus_rg rg ON rg.rg_id = rep.rg_id
JOIN medium m ON m.release = rep.release_id
JOIN track t ON t.medium = m.id AND t.is_data_track IS NOT TRUE
JOIN recording rec ON rec.id = t.recording
WHERE rec.gid IS NOT NULL
  AND COALESCE(NULLIF(btrim(t.name), ''), rec.name) IS NOT NULL;
CREATE INDEX ON stage_recordings (recording_mbid);

-- 5. Version family among OUR recordings only (both endpoints imported -> no dangling edges).
DROP TABLE IF EXISTS version_types;
CREATE TABLE version_types AS
SELECT id, name FROM link_type
WHERE entity_type0 = 'recording' AND entity_type1 = 'recording'
  AND name IN ('other versions','edit','remaster','a cappella','instrumental','karaoke','remix');

DROP TABLE IF EXISTS staged_rec_gids;
CREATE TABLE staged_rec_gids AS SELECT DISTINCT recording_mbid AS gid FROM stage_recordings;
CREATE INDEX ON staged_rec_gids (gid);

DROP TABLE IF EXISTS stage_covers;
CREATE TABLE stage_covers AS
SELECT DISTINCT
  r0.gid AS original_mbid,
  r1.gid AS cover_mbid,
  vt.name AS relation
FROM l_recording_recording lrr
JOIN link l ON l.id = lrr.link
JOIN version_types vt ON vt.id = l.link_type
JOIN recording r0 ON r0.id = lrr.entity0
JOIN recording r1 ON r1.id = lrr.entity1
JOIN staged_rec_gids g0 ON g0.gid = r0.gid
JOIN staged_rec_gids g1 ON g1.gid = r1.gid
WHERE r0.gid <> r1.gid;

\echo DISTILL_DONE

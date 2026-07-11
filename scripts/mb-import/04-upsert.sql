-- Upsert the distilled catalogue into the LIVE Grimoire database, by MBID.
-- Runs against grimoire-postgres-dev. The staging tables in schema mb_import are loaded
-- (by run.sh) from the TSVs the distillation produced. This script does the set-based
-- upsert.
--
-- CRITICAL (brief / D5 / D19): the upsert MUST NOT clobber existing enrichment. On an
-- existing row we only refresh STRUCTURAL fields and never touch:
--   artists:  listeners, rank, embedding, preview_url, links, abstract, image_url,
--             death_date, death_place, xy_x, xy_y
--   releases: artist_id (D29 first-importer owns splits), cover_url
--   edges:    (nothing enrichment-bearing; dates/instruments are structural and merged)
-- New rows get gen_random_uuid() ids and land with the enrichment columns NULL — the
-- coordinator enriches them later (lazy — D5/D19).

\set ON_ERROR_STOP on
BEGIN;

-- Labels first: releases FK label_id.
INSERT INTO labels (id, mbid, name, country)
SELECT gen_random_uuid(), s.mbid, s.name, s.country
FROM mb_import.stage_labels s
ON CONFLICT (mbid) DO UPDATE SET
    name    = EXCLUDED.name,
    country = COALESCE(EXCLUDED.country, labels.country);

-- Artists. Structural only on conflict; enrichment columns untouched.
-- country/city/year use COALESCE(new, existing) so a null in the dump never nulls a value
-- that WS/2 already gave an existing row. tags overwrite only when the dump has some.
-- links deliberately absent from the DO UPDATE SET: existing (enriched) links are preserved;
-- new rows get the staged links.
INSERT INTO artists (id, mbid, name, sort_name, kind, country, city,
                     formed_year, dissolved_year, tags, links)
SELECT gen_random_uuid(), s.mbid, s.name, s.sort_name, s.kind, s.country, s.city,
       s.formed_year, s.dissolved_year, COALESCE(s.tags, '{}'), s.links
FROM mb_import.stage_artists s
ON CONFLICT (mbid) DO UPDATE SET
    name           = EXCLUDED.name,
    sort_name      = EXCLUDED.sort_name,
    kind           = EXCLUDED.kind,
    country        = COALESCE(EXCLUDED.country, artists.country),
    city           = COALESCE(EXCLUDED.city, artists.city),
    formed_year    = COALESCE(EXCLUDED.formed_year, artists.formed_year),
    dissolved_year = COALESCE(EXCLUDED.dissolved_year, artists.dissolved_year),
    tags           = CASE WHEN cardinality(EXCLUDED.tags) > 0
                          THEN EXCLUDED.tags ELSE artists.tags END;

-- Releases. Resolve artist_mbid/label_mbid to internal ids via the just-upserted rows.
-- On conflict keep artist_id (D29) and cover_url; refresh title/type/date/label.
INSERT INTO releases (id, mbid, artist_id, title, type, release_date, label_id)
SELECT gen_random_uuid(), s.mbid, a.id, s.title, s.type, s.release_date, l.id
FROM mb_import.stage_releases s
JOIN artists a ON a.mbid = s.artist_mbid
LEFT JOIN labels l ON l.mbid = s.label_mbid
ON CONFLICT (mbid) DO UPDATE SET
    title        = EXCLUDED.title,
    type         = EXCLUDED.type,
    release_date = COALESCE(EXCLUDED.release_date, releases.release_date),
    label_id     = COALESCE(EXCLUDED.label_id, releases.label_id);

-- Edges. Both endpoints must resolve to corpus artists (INNER JOIN). Unique on
-- (from_id, to_id, kind); merge dates/instruments non-destructively on conflict.
INSERT INTO artist_edges (id, from_id, to_id, kind, begin_date, end_date, instruments)
SELECT gen_random_uuid(), f.id, t.id, s.kind, s.begin_date, s.end_date,
       COALESCE(s.instruments, '{}')
FROM mb_import.stage_edges s
JOIN artists f ON f.mbid = s.from_mbid
JOIN artists t ON t.mbid = s.to_mbid
WHERE f.id <> t.id
ON CONFLICT (from_id, to_id, kind) DO UPDATE SET
    begin_date  = COALESCE(EXCLUDED.begin_date, artist_edges.begin_date),
    end_date    = COALESCE(EXCLUDED.end_date, artist_edges.end_date),
    instruments = CASE WHEN cardinality(EXCLUDED.instruments) > 0
                       THEN EXCLUDED.instruments ELSE artist_edges.instruments END;

COMMIT;

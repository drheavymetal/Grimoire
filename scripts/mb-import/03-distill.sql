-- Distil the metal/rock/folk subgraph from the temporary MB Postgres into staging tables
-- already shaped like Grimoire (enum values as EF enum names, MB gid as the natural key).
-- run.sh COPYs these out and upserts them into the live Grimoire DB by MBID (04-upsert.sql).
--
-- Corpus (DECISIONS D23) = existing Grimoire rows  ∪  genre-tag allowlist  ∪  member-of graph
-- expansion (2 hops). Verified MB facts used below:
--   * member of band  = link_type 103; entity0 = member (Person), entity1 = band.
--   * instrument attributes = link_attribute_type with root IN (14 instrument, 3 vocal).
--   * guest membership     = a link carrying an attribute with root 194 (guest) -> excluded.
--   * MB has NO artist-artist 'influenced by' relation, so influence stays Wikidata-only (D3).
SET synchronous_commit = off;
SET work_mem = '512MB';
-- Serial execution: parallel hash joins allocate /dev/shm segments, and a container's
-- default /dev/shm is only 64 MB, which overflows on the big joins ("could not resize
-- shared memory segment ... No space left on device"). run.sh also passes --shm-size=1g;
-- this makes the script safe even without it.
SET max_parallel_workers_per_gather = 0;

-- ---------------------------------------------------------------------------
-- 0. Existing Grimoire artists (so nothing already present is dropped and the
--    folk anchors / current roots get enriched). run.sh creates existing_gid and
--    \copies stage/existing_gids.txt into it BEFORE running this script, so here we
--    only guard its existence — never drop it (that would wipe the loaded rows).
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS existing_gid (gid uuid);

-- ---------------------------------------------------------------------------
-- 1. Genre-tag allowlist (D23): broad metal/rock/folk. NEVER bare 'folk'/'rock'/'pop'.
--    '%metal%' captures every metal subgenre (black/death/doom/thrash/heavy/power/
--    folk/viking/... metal, metalcore, post-metal). The explicit list adds metal-adjacent
--    genres that do not contain the word 'metal', the orbiting hard rock / punk, and the
--    named folk subgenres. count >= 1 keeps net-upvoted tags only (drops downvoted noise).
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS genre_tags;
CREATE TABLE genre_tags AS
SELECT id FROM tag
WHERE lower(name) LIKE '%metal%'
   OR lower(name) IN (
     -- metal-adjacent extreme / hardcore / punk that does not contain 'metal'
     'grindcore','goregrind','deathcore','powerviolence','mathcore','crossover thrash',
     'crust','crust punk','d-beat','djent','blackgaze','dungeon synth','sludge','doom',
     'stoner','drone','noise rock','post-hardcore','metalcore',
     -- hard rock / rock orbit
     'hard rock','heavy rock','stoner rock','psychedelic rock','progressive rock',
     'gothic rock','southern rock','blues rock','garage rock','acid rock','proto-metal',
     -- punk orbit
     'punk','punk rock','hardcore punk','melodic hardcore','post-punk','psychobilly','horror punk',
     -- folk subgenres (never bare 'folk')
     'neofolk','viking folk','nordic folk','pagan folk','celtic folk','dark folk',
     'ritual folk','medieval folk','folk metal','martial industrial','neoclassical darkwave'
   );

-- ---------------------------------------------------------------------------
-- 2. Corpus assembly. corpus(artist_id) is the set of MB artist.id we import.
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS corpus;
CREATE TABLE corpus (artist_id INT PRIMARY KEY);

-- 2a. existing Grimoire rows
INSERT INTO corpus
SELECT a.id FROM artist a JOIN existing_gid e ON e.gid = a.gid
ON CONFLICT DO NOTHING;

-- 2b. tag-matched artists (net-positive genre votes)
INSERT INTO corpus
SELECT DISTINCT at.artist
FROM artist_tag at JOIN genre_tags g ON g.id = at.tag
WHERE at.count >= 1
ON CONFLICT DO NOTHING;

-- 2c. member-of edges among ALL artists (both directions kept for undirected expansion)
DROP TABLE IF EXISTS mem_links;
CREATE TABLE mem_links AS
SELECT laa.entity0 AS member_id, laa.entity1 AS band_id, laa.link
FROM l_artist_artist laa
JOIN link l ON l.id = laa.link
WHERE l.link_type = 103;
CREATE INDEX ON mem_links (member_id);
CREATE INDEX ON mem_links (band_id);

-- 2d. graph expansion, 2 hops. Each hop adds: the members of bands in the corpus, and the
--     other bands those members belong to (D23 admission-by-bloodline).
--     Hop 1:
INSERT INTO corpus SELECT DISTINCT m.member_id FROM mem_links m JOIN corpus c ON c.artist_id = m.band_id   ON CONFLICT DO NOTHING;
INSERT INTO corpus SELECT DISTINCT m.band_id   FROM mem_links m JOIN corpus c ON c.artist_id = m.member_id ON CONFLICT DO NOTHING;
--     Hop 2:
INSERT INTO corpus SELECT DISTINCT m.member_id FROM mem_links m JOIN corpus c ON c.artist_id = m.band_id   ON CONFLICT DO NOTHING;
INSERT INTO corpus SELECT DISTINCT m.band_id   FROM mem_links m JOIN corpus c ON c.artist_id = m.member_id ON CONFLICT DO NOTHING;

ANALYZE corpus;

-- ---------------------------------------------------------------------------
-- 3. Per-artist derived pieces: top-8 tags, links jsonb.
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS artist_top_tags;
CREATE TABLE artist_top_tags AS
SELECT artist, array_agg(name ORDER BY cnt DESC) AS tags
FROM (
  SELECT at.artist, t.name, at.count AS cnt,
         row_number() OVER (PARTITION BY at.artist ORDER BY at.count DESC, t.name) AS rn
  FROM artist_tag at
  JOIN corpus c ON c.artist_id = at.artist
  JOIN tag t ON t.id = at.tag
  WHERE at.count >= 1
) s
WHERE rn <= 8
GROUP BY artist;
CREATE INDEX ON artist_top_tags (artist);

DROP TABLE IF EXISTS artist_links;
CREATE TABLE artist_links AS
SELECT artist, jsonb_object_agg(rel_name, url) AS links
FROM (
  SELECT DISTINCT ON (lau.entity0, lt.name)
         lau.entity0 AS artist, lt.name AS rel_name, u.url AS url
  FROM l_artist_url lau
  JOIN corpus c ON c.artist_id = lau.entity0
  JOIN link l ON l.id = lau.link
  JOIN link_type lt ON lt.id = l.link_type
  JOIN url u ON u.id = lau.entity1
  ORDER BY lau.entity0, lt.name, lau.id
) d
GROUP BY artist;
CREATE INDEX ON artist_links (artist);

-- ---------------------------------------------------------------------------
-- 4. stage_artists (Grimoire shape). kind mapped like MbMapping.MapKind. country via
--    area -> iso_3166_1 (null when the area is sub-country: honest gap, never invented).
--    city from begin_area's name. formed/dissolved = begin/end year.
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS stage_artists;
CREATE TABLE stage_artists AS
SELECT
  a.gid AS mbid,
  a.name,
  a.sort_name,
  CASE at.name WHEN 'Person' THEN 'Person' WHEN 'Orchestra' THEN 'Orchestra'
               WHEN 'Choir' THEN 'Choir' ELSE 'Group' END AS kind,
  iso.code AS country,
  ba.name AS city,
  a.begin_date_year AS formed_year,
  a.end_date_year AS dissolved_year,
  COALESCE(tt.tags, '{}') AS tags,
  al.links AS links
FROM corpus c
JOIN artist a ON a.id = c.artist_id
LEFT JOIN artist_type at ON at.id = a.type
LEFT JOIN iso_3166_1 iso ON iso.area = a.area
LEFT JOIN area ba ON ba.id = a.begin_area
LEFT JOIN artist_top_tags tt ON tt.artist = a.id
LEFT JOIN artist_links al ON al.artist = a.id;

-- ---------------------------------------------------------------------------
-- 5. stage_edges (member_of). Per (member, band): min begin, open-end wins, union of
--    instruments. Guest memberships excluded. day forced to 1 (MB member dates are
--    year/month precision in practice; forcing day=1 matches the existing Grimoire edges
--    and is crash-proof against any rare invalid day-in-month).
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS link_instruments;
CREATE TABLE link_instruments AS
SELECT la.link, array_agg(DISTINCT lat.name) AS instruments
FROM link_attribute la
JOIN link_attribute_type lat ON lat.id = la.attribute_type
WHERE lat.root IN (14, 3)          -- instrument + vocal trees
GROUP BY la.link;
CREATE INDEX ON link_instruments (link);

DROP TABLE IF EXISTS guest_links;
CREATE TABLE guest_links AS
SELECT DISTINCT la.link
FROM link_attribute la
JOIN link_attribute_type lat ON lat.id = la.attribute_type
WHERE lat.root = 194;              -- guest
CREATE INDEX ON guest_links (link);

-- One row per membership link among corpus pairs (guests excluded), dates already parsed.
DROP TABLE IF EXISTS mem_pairs;
CREATE TABLE mem_pairs AS
SELECT
  laa.link,
  laa.entity0 AS member_id,
  laa.entity1 AS band_id,
  CASE WHEN l.begin_date_year IS NULL THEN NULL
       ELSE make_date(l.begin_date_year, COALESCE(NULLIF(l.begin_date_month,0),1), 1) END AS begin_date,
  CASE WHEN l.end_date_year IS NULL THEN NULL
       ELSE make_date(l.end_date_year, COALESCE(NULLIF(l.end_date_month,0),1), 1) END AS end_date,
  l.ended
FROM l_artist_artist laa
JOIN corpus c0 ON c0.artist_id = laa.entity0
JOIN corpus c1 ON c1.artist_id = laa.entity1
JOIN link l ON l.id = laa.link
WHERE l.link_type = 103
  AND laa.link NOT IN (SELECT link FROM guest_links);

-- Union of instruments per (member, band): unnest each link's instrument array first,
-- then aggregate the scalar names (array_agg of variable-length arrays is illegal).
DROP TABLE IF EXISTS edge_instruments;
CREATE TABLE edge_instruments AS
SELECT mp.member_id, mp.band_id, array_agg(DISTINCT ins ORDER BY ins) AS instruments
FROM mem_pairs mp
JOIN link_instruments li ON li.link = mp.link,
     LATERAL unnest(li.instruments) AS ins
GROUP BY mp.member_id, mp.band_id;

-- Merge stints: min begin, open-end wins (any not-ended stint -> null end), union instruments.
DROP TABLE IF EXISTS stage_edges;
CREATE TABLE stage_edges AS
SELECT
  ma.gid AS from_mbid,
  ba.gid AS to_mbid,
  'MemberOf' AS kind,
  min(mp.begin_date) AS begin_date,
  CASE WHEN bool_or(NOT mp.ended) THEN NULL ELSE max(mp.end_date) END AS end_date,
  COALESCE(ei.instruments, '{}') AS instruments
FROM mem_pairs mp
JOIN artist ma ON ma.id = mp.member_id
JOIN artist ba ON ba.id = mp.band_id
LEFT JOIN edge_instruments ei ON ei.member_id = mp.member_id AND ei.band_id = mp.band_id
GROUP BY ma.gid, ba.gid, ei.instruments;

-- ---------------------------------------------------------------------------
-- 6. stage_releases. Attribute each release-group to ONE corpus artist (min credit
--    position, then min artist id) so the release MBID stays unique (D29 first-importer).
--    type = primary/secondary map (matching MusicBrainzSeedJob.MapReleaseType precedence);
--    Single/Broadcast/Other are dropped (Grimoire has no Single type). date from
--    release_group_meta. label from the lowest-id release of the group that carries one.
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS rg_artist;
CREATE TABLE rg_artist AS
SELECT DISTINCT ON (rg.id)
  rg.id AS rg_id, rg.gid AS mbid, rg.name AS title, rg.type AS prim_type, acn.artist AS artist_id
FROM release_group rg
JOIN artist_credit_name acn ON acn.artist_credit = rg.artist_credit
JOIN corpus c ON c.artist_id = acn.artist
ORDER BY rg.id, acn.position, acn.artist;
CREATE INDEX ON rg_artist (rg_id);

DROP TABLE IF EXISTS rg_sec;
CREATE TABLE rg_sec AS
SELECT j.release_group AS rg_id,
       bool_or(st.name = 'Demo') AS is_demo,
       bool_or(st.name = 'Compilation') AS is_comp,
       bool_or(st.name = 'Live') AS is_live
FROM release_group_secondary_type_join j
JOIN release_group_secondary_type st ON st.id = j.secondary_type
WHERE j.release_group IN (SELECT rg_id FROM rg_artist)
GROUP BY j.release_group;
CREATE INDEX ON rg_sec (rg_id);

DROP TABLE IF EXISTS rg_label;
CREATE TABLE rg_label AS
SELECT DISTINCT ON (r.release_group) r.release_group AS rg_id, lbl.gid AS label_mbid
FROM release r
JOIN release_label rl ON rl.release = r.id
JOIN label lbl ON lbl.id = rl.label
WHERE r.release_group IN (SELECT rg_id FROM rg_artist)
ORDER BY r.release_group, r.id;
CREATE INDEX ON rg_label (rg_id);

DROP TABLE IF EXISTS stage_releases;
CREATE TABLE stage_releases AS
SELECT
  ra.mbid,
  a.gid AS artist_mbid,
  ra.title,
  CASE
    WHEN COALESCE(s.is_demo, false) THEN 'Demo'
    WHEN COALESCE(s.is_comp, false) THEN 'Compilation'
    WHEN COALESCE(s.is_live, false) THEN 'Live'
    WHEN pt.name = 'Album' THEN 'Album'
    WHEN pt.name = 'EP' THEN 'Ep'
    ELSE NULL
  END AS type,
  CASE WHEN m.first_release_date_year IS NULL THEN NULL
       ELSE make_date(m.first_release_date_year, COALESCE(NULLIF(m.first_release_date_month,0),1),
                      COALESCE(NULLIF(m.first_release_date_day,0),1)) END AS release_date,
  rl.label_mbid
FROM rg_artist ra
JOIN artist a ON a.id = ra.artist_id
LEFT JOIN release_group_primary_type pt ON pt.id = ra.prim_type
LEFT JOIN rg_sec s ON s.rg_id = ra.rg_id
LEFT JOIN release_group_meta m ON m.id = ra.rg_id
LEFT JOIN rg_label rl ON rl.rg_id = ra.rg_id;

DELETE FROM stage_releases WHERE type IS NULL;   -- drop Single/Broadcast/Other/typeless

-- ---------------------------------------------------------------------------
-- 7. stage_labels: only labels referenced by staged releases. country via area->iso.
-- ---------------------------------------------------------------------------
DROP TABLE IF EXISTS stage_labels;
CREATE TABLE stage_labels AS
SELECT DISTINCT lbl.gid AS mbid, lbl.name, iso.code AS country
FROM label lbl
JOIN (SELECT DISTINCT label_mbid FROM stage_releases WHERE label_mbid IS NOT NULL) x
  ON x.label_mbid = lbl.gid
LEFT JOIN iso_3166_1 iso ON iso.area = lbl.area;

\echo DISTILL_DONE

#!/usr/bin/env bash
# Grimoire — full MusicBrainz dump import (DECISIONS D5: the MB mirror is a build artifact).
#
# Replaces the toy corpus with the real metal/rock/folk catalogue distilled from the MB dump.
# End-to-end and idempotent: re-running upserts by MBID and never clobbers enrichment.
#
# Prereqs:
#   - MB dumps at $MB_DIR (mbdump.tar.bz2 core, mbdump-derived.tar.bz2 tags).
#   - The live Grimoire dev DB running in container $GRIMOIRE_CONTAINER (localhost:5433).
#   - docker image postgres:16-alpine.
#
# The temporary MB Postgres ($MB_CONTAINER, port 5434) is thrown away after; it is NOT
# production (D5). Dumps and scratch live under $MB_DIR, outside the repo.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MB_DIR="${MB_DIR:-/var/tmp/grimoire-mb}"
STAGE="$MB_DIR/stage"
MB_CONTAINER="${MB_CONTAINER:-grimoire-mb-import}"
GRIMOIRE_CONTAINER="${GRIMOIRE_CONTAINER:-grimoire-postgres-dev}"
MBDUMP="$MB_DIR/mbdump"

CORE_TABLES="artist artist_type area iso_3166_1 l_artist_artist link link_type \
  link_attribute link_attribute_type release_group release_group_primary_type \
  release_group_secondary_type release_group_secondary_type_join \
  release artist_credit artist_credit_name label release_label url l_artist_url"
# release_group_meta is DERIVED data (first_release_date), so it lives in the derived tar,
# NOT the core tar — extracting it from core fails with "not found in archive".
DERIVED_TABLES="tag artist_tag release_group_meta"

mbsql() { docker exec -i "$MB_CONTAINER" psql -U mb -d mb -v ON_ERROR_STOP=1 "$@"; }
grsql() { docker exec -i "$GRIMOIRE_CONTAINER" psql -U grimoire -d grimoire -v ON_ERROR_STOP=1 "$@"; }

echo "== Phase 1: extract needed tables from the tars (one decompression pass each) =="
mkdir -p "$MBDUMP" "$STAGE"
tar xjf "$MB_DIR/mbdump.tar.bz2"        -C "$MB_DIR" $(printf 'mbdump/%s ' $CORE_TABLES)
tar xjf "$MB_DIR/mbdump-derived.tar.bz2" -C "$MB_DIR" $(printf 'mbdump/%s ' $DERIVED_TABLES)

echo "== Phase 2: start the temporary MB Postgres and load =="
docker rm -f "$MB_CONTAINER" >/dev/null 2>&1 || true
docker run -d --name "$MB_CONTAINER" \
  -e POSTGRES_USER=mb -e POSTGRES_PASSWORD=mb -e POSTGRES_DB=mb -p 5434:5432 \
  --shm-size=1g -v "$MB_DIR:/dump:ro" postgres:16-alpine \
  -c fsync=off -c synchronous_commit=off -c full_page_writes=off \
  -c shared_buffers=2GB -c work_mem=512MB -c maintenance_work_mem=2GB \
  -c max_wal_size=8GB -c checkpoint_timeout=30min >/dev/null
until docker exec "$MB_CONTAINER" pg_isready -U mb >/dev/null 2>&1; do sleep 1; done
mbsql < "$HERE/01-load-schema.sql"
mbsql < "$HERE/02-copy-and-index.sql"

echo "== Phase 3: distil the subgraph (corpus D23) =="
# Seed the corpus with the artists already in Grimoire so nothing present is dropped and the
# folk anchors get enriched. Load existing_gid BEFORE distill; 03 only guards its existence.
grsql -tAc "select mbid from artists;" > "$STAGE/existing_gids.txt"
mbsql -c "DROP TABLE IF EXISTS existing_gid; CREATE TABLE existing_gid (gid uuid);"
docker exec -i "$MB_CONTAINER" psql -U mb -d mb -c "\copy existing_gid FROM STDIN" < "$STAGE/existing_gids.txt"
mbsql < "$HERE/03-distill.sql"

echo "== Phase 4: transfer staging tables to Grimoire and upsert =="
grsql <<'SQL'
CREATE SCHEMA IF NOT EXISTS mb_import;
DROP TABLE IF EXISTS mb_import.stage_artists, mb_import.stage_edges,
                     mb_import.stage_releases, mb_import.stage_labels;
CREATE TABLE mb_import.stage_artists (mbid uuid, name text, sort_name text, kind text,
  country text, city text, formed_year int, dissolved_year int, tags text[], links jsonb);
CREATE TABLE mb_import.stage_edges (from_mbid uuid, to_mbid uuid, kind text,
  begin_date date, end_date date, instruments text[]);
CREATE TABLE mb_import.stage_releases (mbid uuid, artist_mbid uuid, title text, type text,
  release_date date, label_mbid uuid);
CREATE TABLE mb_import.stage_labels (mbid uuid, name text, country text);
SQL

for t in stage_artists stage_edges stage_releases stage_labels; do
  docker exec "$MB_CONTAINER" psql -U mb -d mb -c "\copy $t TO STDOUT" \
    | grsql -c "\copy mb_import.$t FROM STDIN"
done

grsql < "$HERE/04-upsert.sql"
grsql -c "DROP SCHEMA mb_import CASCADE;"

echo "== Done. The MB temp container ($MB_CONTAINER) can be stopped/removed; it is throwaway. =="

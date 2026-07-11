#!/usr/bin/env bash
# Grimoire — MusicBrainz recordings/tracks/covers import (DECISIONS D5: the MB mirror is a
# build artifact). Unlocks C7 (track duration), C21 (song titles) and C10 (version graph).
#
# Distils, for every release already in Grimoire, its tracklist (title + length + position)
# and the recording->recording "covers and versions" edges among our recordings. End-to-end
# and idempotent: re-running upserts by (release_id, position) / (original, cover) and touches
# no other table.
#
# Prereqs:
#   - MB core dump at $MB_DIR/mbdump.tar.bz2.
#   - The live Grimoire dev DB running in $GRIMOIRE_CONTAINER (localhost:5433), with the
#     AddRecordingsAndCoverVersions migration applied.
#   - docker image postgres:16-alpine.
#
# The temporary MB Postgres ($MB_CONTAINER, port 5435) is thrown away after; it is NOT
# production (D5) and is separate from the artists-import container (port 5434).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MB_DIR="${MB_DIR:-/var/tmp/grimoire-mb}"
STAGE="$MB_DIR/stage"
MB_CONTAINER="${MB_CONTAINER:-grimoire-mb-recordings}"
GRIMOIRE_CONTAINER="${GRIMOIRE_CONTAINER:-grimoire-postgres-dev}"
MBDUMP="$MB_DIR/mbdump"

# release_group + release map our release-group gids to their releases; medium/track/recording
# are the tracklist; l_recording_recording + link + link_type are the version graph (C10).
NEEDED="release_group release medium track recording l_recording_recording link link_type"

mbsql() { docker exec -i "$MB_CONTAINER" psql -U mb -d mb -v ON_ERROR_STOP=1 "$@"; }
grsql() { docker exec -i "$GRIMOIRE_CONTAINER" psql -U grimoire -d grimoire -v ON_ERROR_STOP=1 "$@"; }

echo "== Phase 1: extract the needed tables (one member per tar pass — GNU tar drops trailing"
echo "            members when several are named at once, so extract each separately) =="
mkdir -p "$MBDUMP" "$STAGE"
for tbl in $NEEDED; do
  if [ ! -s "$MBDUMP/$tbl" ]; then
    echo "   extracting $tbl"
    tar xjf "$MB_DIR/mbdump.tar.bz2" "mbdump/$tbl"
  else
    echo "   $tbl already present"
  fi
done

echo "== Phase 2: start the temporary MB Postgres and load =="
docker rm -f "$MB_CONTAINER" >/dev/null 2>&1 || true
docker run -d --name "$MB_CONTAINER" \
  -e POSTGRES_USER=mb -e POSTGRES_PASSWORD=mb -e POSTGRES_DB=mb -p 5435:5432 \
  --shm-size=1g -v "$MB_DIR:/dump:ro" postgres:16-alpine \
  -c fsync=off -c synchronous_commit=off -c full_page_writes=off \
  -c shared_buffers=2GB -c work_mem=512MB -c maintenance_work_mem=2GB \
  -c max_wal_size=8GB -c checkpoint_timeout=30min >/dev/null
until docker exec "$MB_CONTAINER" pg_isready -U mb >/dev/null 2>&1; do sleep 1; done
mbsql < "$HERE/10-load-schema.sql"
mbsql < "$HERE/11-copy-and-index.sql"

echo "== Phase 3: distil the tracklists + version edges for our releases =="
# Load our release-group gids (releases.mbid) BEFORE distill; 12 only guards existence.
grsql -tAc "select mbid from releases;" > "$STAGE/our_rg.txt"
mbsql -c "DROP TABLE IF EXISTS our_rg; CREATE TABLE our_rg (gid uuid);"
docker exec -i "$MB_CONTAINER" psql -U mb -d mb -c "\copy our_rg FROM STDIN" < "$STAGE/our_rg.txt"
mbsql < "$HERE/12-distill.sql"

echo "== Phase 4: transfer staging tables to Grimoire and upsert =="
grsql <<'SQL'
CREATE SCHEMA IF NOT EXISTS mb_import;
DROP TABLE IF EXISTS mb_import.stage_recordings, mb_import.stage_covers, mb_import.rec_by_mbid;
CREATE TABLE mb_import.stage_recordings (release_mbid uuid, recording_mbid uuid, title text,
  length_ms int, position int);
CREATE TABLE mb_import.stage_covers (original_mbid uuid, cover_mbid uuid, relation text);
SQL

for t in stage_recordings stage_covers; do
  docker exec "$MB_CONTAINER" psql -U mb -d mb -c "\copy $t TO STDOUT" \
    | grsql -c "\copy mb_import.$t FROM STDIN"
done

grsql < "$HERE/13-upsert.sql"
grsql -c "DROP SCHEMA mb_import CASCADE;"

echo "== Done. The MB temp container ($MB_CONTAINER) can be stopped/removed; it is throwaway. =="

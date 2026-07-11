-- Grimoire — MusicBrainz recordings/tracks import (DECISIONS D5: the MB mirror is a build
-- artifact). Schema for the TEMPORARY MB Postgres of THIS pass (recordings/tracks/covers),
-- separate from the artists/releases import container. Column orders match the MB production
-- schema exactly (admin/sql/CreateTables.sql) so `COPY ... FROM '<file>'` lines up. Columns
-- Grimoire does not consume are typed TEXT; columns used in joins/output keep real types.
--
-- This container only loads what the recordings distillation needs:
--   release_group + release  -> map our releases.mbid (a release-group gid) to its releases,
--   medium + track + recording -> the tracklist (title, length, position),
--   l_recording_recording + link + link_type -> the "covers and versions" family for C10.

DROP SCHEMA IF EXISTS public CASCADE;
CREATE SCHEMA public;
SET synchronous_commit = off;

CREATE TABLE release_group (
    id INT, gid UUID, name TEXT, artist_credit TEXT, type TEXT, comment TEXT,
    edits_pending TEXT, last_updated TEXT);

CREATE TABLE release (
    id INT, gid TEXT, name TEXT, artist_credit TEXT, release_group INT, status TEXT,
    packaging TEXT, language TEXT, script TEXT, barcode TEXT, comment TEXT,
    edits_pending TEXT, quality TEXT, last_updated TEXT);

CREATE TABLE medium (
    id INT, release INT, position INT, format TEXT, name TEXT,
    edits_pending TEXT, last_updated TEXT, track_count INT, gid TEXT);

CREATE TABLE track (
    id INT, gid TEXT, recording INT, medium INT, position INT, number TEXT, name TEXT,
    artist_credit TEXT, length INT, edits_pending TEXT, last_updated TEXT,
    is_data_track BOOLEAN);

CREATE TABLE recording (
    id INT, gid UUID, name TEXT, artist_credit TEXT, length INT, comment TEXT,
    edits_pending TEXT, last_updated TEXT, video TEXT);

CREATE TABLE l_recording_recording (
    id INT, link INT, entity0 INT, entity1 INT, edits_pending TEXT,
    last_updated TEXT, link_order TEXT, entity0_credit TEXT, entity1_credit TEXT);

CREATE TABLE link (
    id INT, link_type INT,
    begin_date_year TEXT, begin_date_month TEXT, begin_date_day TEXT,
    end_date_year TEXT, end_date_month TEXT, end_date_day TEXT,
    attribute_count TEXT, created TEXT, ended TEXT);

CREATE TABLE link_type (
    id INT, parent TEXT, child_order TEXT, gid TEXT, entity_type0 TEXT, entity_type1 TEXT,
    name TEXT, description TEXT, link_phrase TEXT, reverse_link_phrase TEXT,
    long_link_phrase TEXT, last_updated TEXT, is_deprecated TEXT, has_dates TEXT,
    entity0_cardinality TEXT, entity1_cardinality TEXT);

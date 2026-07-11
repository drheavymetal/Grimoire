-- Grimoire — MusicBrainz dump import (DECISIONS D5: the MB mirror is a build artifact).
-- Schema for the TEMPORARY MB Postgres. Columns match the MB production schema order
-- exactly (from admin/sql/CreateTables.sql), so `COPY ... FROM '<file>'` lines up. Columns
-- Grimoire does not consume are typed TEXT; columns used in joins/output keep real types.
-- Unused MB tables (gender, artist_alias) are intentionally NOT loaded: Grimoire has no
-- target column for them. See docs/progress/mb-dump-import.md.

DROP SCHEMA IF EXISTS public CASCADE;
CREATE SCHEMA public;
SET synchronous_commit = off;

CREATE TABLE artist (
    id INT, gid UUID, name TEXT, sort_name TEXT,
    begin_date_year INT, begin_date_month INT, begin_date_day INT,
    end_date_year INT, end_date_month INT, end_date_day INT,
    type INT, area INT, gender INT, comment TEXT, edits_pending TEXT,
    last_updated TEXT, ended TEXT, begin_area INT, end_area INT);

CREATE TABLE artist_type (
    id INT, name TEXT, parent TEXT, child_order TEXT, description TEXT, gid TEXT);

CREATE TABLE area (
    id INT, gid TEXT, name TEXT, type TEXT, edits_pending TEXT, last_updated TEXT,
    begin_date_year TEXT, begin_date_month TEXT, begin_date_day TEXT,
    end_date_year TEXT, end_date_month TEXT, end_date_day TEXT, ended TEXT, comment TEXT);

CREATE TABLE iso_3166_1 (area INT, code TEXT);

CREATE TABLE l_artist_artist (
    id INT, link INT, entity0 INT, entity1 INT, edits_pending TEXT,
    last_updated TEXT, link_order TEXT, entity0_credit TEXT, entity1_credit TEXT);

CREATE TABLE link (
    id INT, link_type INT,
    begin_date_year INT, begin_date_month INT, begin_date_day INT,
    end_date_year INT, end_date_month INT, end_date_day INT,
    attribute_count TEXT, created TEXT, ended BOOLEAN);

CREATE TABLE link_type (
    id INT, parent TEXT, child_order TEXT, gid TEXT, entity_type0 TEXT, entity_type1 TEXT,
    name TEXT, description TEXT, link_phrase TEXT, reverse_link_phrase TEXT,
    long_link_phrase TEXT, last_updated TEXT, is_deprecated TEXT, has_dates TEXT,
    entity0_cardinality TEXT, entity1_cardinality TEXT);

CREATE TABLE link_attribute (link INT, attribute_type INT, created TEXT);

CREATE TABLE link_attribute_type (
    id INT, parent TEXT, root INT, child_order TEXT, gid TEXT, name TEXT,
    description TEXT, last_updated TEXT);

CREATE TABLE release_group (
    id INT, gid UUID, name TEXT, artist_credit INT, type INT, comment TEXT,
    edits_pending TEXT, last_updated TEXT);

CREATE TABLE release_group_primary_type (
    id INT, name TEXT, parent TEXT, child_order TEXT, description TEXT, gid TEXT);

CREATE TABLE release_group_secondary_type (
    id INT, name TEXT, parent TEXT, child_order TEXT, description TEXT, gid TEXT);

CREATE TABLE release_group_secondary_type_join (
    release_group INT, secondary_type INT, created TEXT);

CREATE TABLE release_group_meta (
    id INT, release_count TEXT,
    first_release_date_year INT, first_release_date_month INT, first_release_date_day INT,
    rating TEXT, rating_count TEXT);

CREATE TABLE release (
    id INT, gid TEXT, name TEXT, artist_credit INT, release_group INT, status TEXT,
    packaging TEXT, language TEXT, script TEXT, barcode TEXT, comment TEXT,
    edits_pending TEXT, quality TEXT, last_updated TEXT);

CREATE TABLE artist_credit (
    id INT, name TEXT, artist_count TEXT, ref_count TEXT, created TEXT,
    edits_pending TEXT, gid TEXT);

CREATE TABLE artist_credit_name (
    artist_credit INT, position INT, artist INT, name TEXT, join_phrase TEXT);

CREATE TABLE label (
    id INT, gid UUID, name TEXT,
    begin_date_year TEXT, begin_date_month TEXT, begin_date_day TEXT,
    end_date_year TEXT, end_date_month TEXT, end_date_day TEXT,
    label_code TEXT, type TEXT, area INT, comment TEXT, edits_pending TEXT,
    last_updated TEXT, ended TEXT);

CREATE TABLE release_label (
    id INT, release INT, label INT, catalog_number TEXT, last_updated TEXT);

CREATE TABLE url (id INT, gid TEXT, url TEXT, edits_pending TEXT, last_updated TEXT);

CREATE TABLE l_artist_url (
    id INT, link INT, entity0 INT, entity1 INT, edits_pending TEXT,
    last_updated TEXT, link_order TEXT, entity0_credit TEXT, entity1_credit TEXT);

CREATE TABLE tag (id INT, name TEXT, ref_count TEXT);

CREATE TABLE artist_tag (artist INT, tag INT, count INT, last_updated TEXT);

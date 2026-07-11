-- Load the MB dump files into the temporary MB Postgres, then build the indexes the
-- distillation needs. The dump directory is mounted read-only at /dump inside the container.
-- MB text dumps are tab-separated with \N for NULL: the default COPY text format.
SET synchronous_commit = off;
SET maintenance_work_mem = '2GB';

\echo loading artist
COPY artist            FROM '/dump/mbdump/artist'            (FORMAT text);
\echo loading artist_type
COPY artist_type       FROM '/dump/mbdump/artist_type'       (FORMAT text);
\echo loading area
COPY area              FROM '/dump/mbdump/area'              (FORMAT text);
\echo loading iso_3166_1
COPY iso_3166_1        FROM '/dump/mbdump/iso_3166_1'        (FORMAT text);
\echo loading l_artist_artist
COPY l_artist_artist   FROM '/dump/mbdump/l_artist_artist'   (FORMAT text);
\echo loading link
COPY link              FROM '/dump/mbdump/link'              (FORMAT text);
\echo loading link_type
COPY link_type         FROM '/dump/mbdump/link_type'         (FORMAT text);
\echo loading link_attribute
COPY link_attribute    FROM '/dump/mbdump/link_attribute'    (FORMAT text);
\echo loading link_attribute_type
COPY link_attribute_type FROM '/dump/mbdump/link_attribute_type' (FORMAT text);
\echo loading release_group
COPY release_group     FROM '/dump/mbdump/release_group'     (FORMAT text);
\echo loading release_group_primary_type
COPY release_group_primary_type FROM '/dump/mbdump/release_group_primary_type' (FORMAT text);
\echo loading release_group_secondary_type
COPY release_group_secondary_type FROM '/dump/mbdump/release_group_secondary_type' (FORMAT text);
\echo loading release_group_secondary_type_join
COPY release_group_secondary_type_join FROM '/dump/mbdump/release_group_secondary_type_join' (FORMAT text);
\echo loading release_group_meta
COPY release_group_meta FROM '/dump/mbdump/release_group_meta' (FORMAT text);
\echo loading release
COPY release           FROM '/dump/mbdump/release'           (FORMAT text);
\echo loading artist_credit
COPY artist_credit     FROM '/dump/mbdump/artist_credit'     (FORMAT text);
\echo loading artist_credit_name
COPY artist_credit_name FROM '/dump/mbdump/artist_credit_name' (FORMAT text);
\echo loading label
COPY label             FROM '/dump/mbdump/label'             (FORMAT text);
\echo loading release_label
COPY release_label     FROM '/dump/mbdump/release_label'     (FORMAT text);
\echo loading url
COPY url               FROM '/dump/mbdump/url'               (FORMAT text);
\echo loading l_artist_url
COPY l_artist_url      FROM '/dump/mbdump/l_artist_url'      (FORMAT text);
\echo loading tag
COPY tag               FROM '/dump/mbdump/tag'               (FORMAT text);
\echo loading artist_tag
COPY artist_tag        FROM '/dump/mbdump/artist_tag'        (FORMAT text);

\echo building indexes
ALTER TABLE artist ADD PRIMARY KEY (id);
CREATE INDEX ON artist (area);
CREATE INDEX ON artist (begin_area);
CREATE INDEX ON artist (type);
ALTER TABLE artist_type ADD PRIMARY KEY (id);
ALTER TABLE area ADD PRIMARY KEY (id);
CREATE INDEX ON iso_3166_1 (area);
CREATE INDEX ON l_artist_artist (entity0);
CREATE INDEX ON l_artist_artist (entity1);
CREATE INDEX ON l_artist_artist (link);
ALTER TABLE link ADD PRIMARY KEY (id);
ALTER TABLE link_type ADD PRIMARY KEY (id);
CREATE INDEX ON link (link_type);
CREATE INDEX ON link_attribute (link);
ALTER TABLE link_attribute_type ADD PRIMARY KEY (id);
ALTER TABLE release_group ADD PRIMARY KEY (id);
CREATE INDEX ON release_group (artist_credit);
ALTER TABLE release_group_primary_type ADD PRIMARY KEY (id);
ALTER TABLE release_group_secondary_type ADD PRIMARY KEY (id);
CREATE INDEX ON release_group_secondary_type_join (release_group);
ALTER TABLE release_group_meta ADD PRIMARY KEY (id);
CREATE INDEX ON release (release_group);
ALTER TABLE release ADD PRIMARY KEY (id);
CREATE INDEX ON artist_credit_name (artist_credit);
CREATE INDEX ON artist_credit_name (artist);
ALTER TABLE label ADD PRIMARY KEY (id);
CREATE INDEX ON release_label (release);
ALTER TABLE url ADD PRIMARY KEY (id);
CREATE INDEX ON l_artist_url (entity0);
CREATE INDEX ON l_artist_url (link);
ALTER TABLE tag ADD PRIMARY KEY (id);
CREATE INDEX ON tag (name);
CREATE INDEX ON artist_tag (tag);
CREATE INDEX ON artist_tag (artist);
\echo done

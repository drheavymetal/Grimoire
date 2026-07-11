-- Load the MB dump files into the temporary MB Postgres, then build the indexes the
-- recordings distillation needs. The dump directory is mounted read-only at /dump.
-- MB text dumps are tab-separated with \N for NULL: the default COPY text format.
SET synchronous_commit = off;
SET maintenance_work_mem = '2GB';

\echo loading release_group
COPY release_group FROM '/dump/mbdump/release_group' (FORMAT text);
\echo loading release
COPY release       FROM '/dump/mbdump/release'       (FORMAT text);
\echo loading medium
COPY medium        FROM '/dump/mbdump/medium'        (FORMAT text);
\echo loading track
COPY track         FROM '/dump/mbdump/track'         (FORMAT text);
\echo loading recording
COPY recording     FROM '/dump/mbdump/recording'     (FORMAT text);
\echo loading l_recording_recording
COPY l_recording_recording FROM '/dump/mbdump/l_recording_recording' (FORMAT text);
\echo loading link
COPY link          FROM '/dump/mbdump/link'          (FORMAT text);
\echo loading link_type
COPY link_type     FROM '/dump/mbdump/link_type'     (FORMAT text);

\echo building indexes
ALTER TABLE release_group ADD PRIMARY KEY (id);
CREATE INDEX ON release_group (gid);
ALTER TABLE release ADD PRIMARY KEY (id);
CREATE INDEX ON release (release_group);
ALTER TABLE medium ADD PRIMARY KEY (id);
CREATE INDEX ON medium (release);
CREATE INDEX ON track (medium);
CREATE INDEX ON track (recording);
ALTER TABLE recording ADD PRIMARY KEY (id);
CREATE INDEX ON recording (gid);
CREATE INDEX ON l_recording_recording (link);
CREATE INDEX ON l_recording_recording (entity0);
CREATE INDEX ON l_recording_recording (entity1);
ALTER TABLE link ADD PRIMARY KEY (id);
ALTER TABLE link_type ADD PRIMARY KEY (id);
\echo done

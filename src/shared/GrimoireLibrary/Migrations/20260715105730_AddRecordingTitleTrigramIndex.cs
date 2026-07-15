using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Library.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordingTitleTrigramIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // GIN trigram index on recording titles, powering the mined-theme lanes (the Rite's mined
            // filter and the browse door): fast ILIKE '%keyword%' over recordings.title. Built
            // CONCURRENTLY so it never locks the (large) recordings table, and IF NOT EXISTS because
            // production already has this exact index built by hand — there the migration is a no-op;
            // on a fresh database it builds it. pg_trgm is already enabled (artist search uses it).
            // CONCURRENTLY cannot run inside a transaction, hence suppressTransaction.
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_recordings_title_trgm ON recordings USING gin (title gin_trgm_ops);",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS ix_recordings_title_trgm;",
                suppressTransaction: true);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Library.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrichmentMarkers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "embedding_fingerprint",
                table: "artists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "listeners_checked_at",
                table: "artists",
                type: "timestamp with time zone",
                nullable: true);

            // No backfill, deliberately. "Checked" is `listeners IS NOT NULL OR listeners_checked_at
            // IS NOT NULL`: a row carrying a listener count proves on its own that we asked. The
            // column exists only to disambiguate the NULL case — "not asked yet" from "asked, and
            // Last.fm has no such artist" — so stamping the ~113k resolved rows would add no
            // information at all.
            //
            // It would also not be free. Postgres rewrites every updated row, and these rows carry
            // a 768-dimension embedding and sit in an HNSW index: an UPDATE of that shape moves
            // hundreds of megabytes and churns the index. An earlier revision of this migration did
            // exactly that, blew past the 30s command timeout, and rolled back mid-flight — while
            // the server-side UPDATE kept running and held EF's exclusive lock on
            // __EFMigrationsHistory, so every subsequent API boot blocked on reading it, timed out,
            // crashed, and restarted into the same wall. It took the production API down.
            //
            // Migrations move schema. Bulk data movement is an operational step, run out of band.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "embedding_fingerprint",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "listeners_checked_at",
                table: "artists");
        }
    }
}

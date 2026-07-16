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

            // Backfill the marker for everything Last.fm already answered about: a row carrying a
            // listener count was, self-evidently, checked. Without this the new marker would read
            // "never asked" for all ~113k resolved artists and a future refresh pass would re-crawl
            // the entire catalogue to learn what it already knows.
            //
            // The misses are deliberately NOT backfilled. They are indistinguishable, today, from
            // artists the pass never reached — the whole reason this column exists (MEMORY §6f) —
            // so they stay unstamped and the next run asks Last.fm once, properly, and stamps them.
            migrationBuilder.Sql(
                "UPDATE artists SET listeners_checked_at = now() WHERE listeners IS NOT NULL;");
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

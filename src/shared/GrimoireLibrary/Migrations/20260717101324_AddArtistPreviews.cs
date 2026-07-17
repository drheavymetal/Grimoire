using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Library.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// The alternate clips of a band (DECISIONS D67), and the marker for the pass that collects them.
    /// One CREATE TABLE and one nullable column, and deliberately nothing else — no backfill, no UPDATE,
    /// not one existing row rewritten.
    ///
    /// `artists.preview_url` is NOT touched, moved, or emptied: it stays exactly what it was, The Rite's
    /// cut, and every draw the engine makes filters on `preview_url IS NOT NULL`. Migrating it into the
    /// new table would have been the tidy thing to do and would have silenced the app.
    ///
    /// `previews_checked_at` lands null on all 206 887 rows and stays there. Adding a nullable column
    /// with no default is a catalogue edit in PostgreSQL — no table rewrite, no touching of the HNSW
    /// index over the 768-dimension embeddings each of those rows carries. Backfilling it is what would
    /// have cost, and it is also what would have been WRONG: null is precisely how the harvest finds the
    /// bands it has never visited, so a backfill would mark the catalogue done and collect nothing, for
    /// ever. That is D61's lesson, and the same reason `listeners_checked_at` left its misses unstamped.
    ///
    /// The shape of a migration here was learned the hard way. An earlier one carried a bulk UPDATE over
    /// `artists`; it moved hundreds of megabytes, blew past the 30 s command timeout and rolled back —
    /// while the server-side UPDATE kept running and held EF's exclusive lock on __EFMigrationsHistory,
    /// so every API boot blocked reading it, timed out, crashed, and restarted into the same wall. It
    /// took production down (MEMORY §6f). Migrations move schema; data moves separately, out of band, as
    /// the `previews` verb.
    /// </remarks>
    public partial class AddArtistPreviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "previews_checked_at",
                table: "artists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "artist_previews",
                columns: table => new
                {
                    artist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    track_title = table.Column<string>(type: "text", nullable: true),
                    collected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artist_previews", x => new { x.artist_id, x.url });
                    table.ForeignKey(
                        name: "fk_artist_previews_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "artist_previews");

            migrationBuilder.DropColumn(
                name: "previews_checked_at",
                table: "artists");
        }
    }
}

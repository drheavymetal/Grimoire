using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Library.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Biographies in every language other than English. One CREATE TABLE, and deliberately nothing
    /// else — no column added to `artists`, no backfill, not a single row rewritten.
    ///
    /// English stays exactly where it is (`artists.abstract`), so this migration does not move the
    /// 34 581 biographies already collected, does not touch `abstract_checked_at`'s 206 882 stamps,
    /// and cannot invalidate an embedding fingerprint (D62). The new table starts empty and the pass
    /// fills it from the network, out of band, as an operational step.
    ///
    /// That is the shape a migration is allowed to have here, and it was learned the hard way: the
    /// previous one carried a bulk UPDATE over `artists`, whose rows hold a 768-dimension embedding
    /// in an HNSW index. It moved hundreds of megabytes, blew past the 30s command timeout, and
    /// rolled back — while the server-side UPDATE kept running and held EF's exclusive lock on
    /// __EFMigrationsHistory, so every API boot blocked reading it, timed out, crashed, and restarted
    /// into the same wall. It took production down (MEMORY §6f). Migrations move schema; data moves
    /// separately.
    ///
    /// Choosing a child table over `abstract_es`/`abstract_url_es` columns is what makes that free:
    /// a new language is INSERTs into a light table, never an UPDATE of a vector-carrying row — and
    /// never another migration, since a language is configuration (`Wikipedia:Languages`).
    /// </remarks>
    public partial class AddBiographyLanguages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "artist_biographies",
                columns: table => new
                {
                    artist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    @abstract = table.Column<string>(name: "abstract", type: "text", nullable: true),
                    abstract_url = table.Column<string>(type: "text", nullable: true),
                    checked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_artist_biographies", x => new { x.artist_id, x.language });
                    table.ForeignKey(
                        name: "fk_artist_biographies_artists_artist_id",
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
                name: "artist_biographies");
        }
    }
}

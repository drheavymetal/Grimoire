using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Library.Migrations
{
    /// <inheritdoc />
    public partial class AddCreditsWorksDeathsAndProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "death_date",
                table: "artists",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "death_place",
                table: "artists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "xy_x",
                table: "artists",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "xy_y",
                table: "artists",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "credits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    artist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recording_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<string>(type: "text", nullable: false),
                    instrument = table.Column<string>(type: "text", nullable: true),
                    is_guest = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credits", x => x.id);
                    table.ForeignKey(
                        name: "fk_credits_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_credits_releases_release_id",
                        column: x => x.release_id,
                        principalTable: "releases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "works",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mbid = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_works", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credits_artist_id",
                table: "credits",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_credits_release_id",
                table: "credits",
                column: "release_id");

            migrationBuilder.CreateIndex(
                name: "ix_works_mbid",
                table: "works",
                column: "mbid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "credits");

            migrationBuilder.DropTable(
                name: "works");

            migrationBuilder.DropColumn(
                name: "death_date",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "death_place",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "xy_x",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "xy_y",
                table: "artists");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Library.Migrations
{
    /// <inheritdoc />
    public partial class RemoveClassicalAddMetalArchives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "works");

            migrationBuilder.AddColumn<string[]>(
                name: "lyrical_themes",
                table: "artists",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<DateTime>(
                name: "metal_archives_checked_at",
                table: "artists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "metal_archives_genre",
                table: "artists",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "metal_archives_id",
                table: "artists",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lyrical_themes",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "metal_archives_checked_at",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "metal_archives_genre",
                table: "artists");

            migrationBuilder.DropColumn(
                name: "metal_archives_id",
                table: "artists");

            migrationBuilder.CreateTable(
                name: "works",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    composer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: true),
                    mbid = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_works", x => x.id);
                    table.ForeignKey(
                        name: "fk_works_artists_composer_id",
                        column: x => x.composer_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_works_composer_id",
                table: "works",
                column: "composer_id");

            migrationBuilder.CreateIndex(
                name: "ix_works_mbid",
                table: "works",
                column: "mbid",
                unique: true);
        }
    }
}

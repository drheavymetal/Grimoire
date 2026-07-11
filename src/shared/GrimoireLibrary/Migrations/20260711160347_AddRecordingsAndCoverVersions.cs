using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Library.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordingsAndCoverVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recordings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    mbid = table.Column<Guid>(type: "uuid", nullable: false),
                    release_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    length_ms = table.Column<int>(type: "integer", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recordings", x => x.id);
                    table.ForeignKey(
                        name: "fk_recordings_releases_release_id",
                        column: x => x.release_id,
                        principalTable: "releases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cover_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cover_recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relation = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cover_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_cover_versions_recordings_cover_recording_id",
                        column: x => x.cover_recording_id,
                        principalTable: "recordings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cover_versions_recordings_original_recording_id",
                        column: x => x.original_recording_id,
                        principalTable: "recordings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cover_versions_cover_recording_id",
                table: "cover_versions",
                column: "cover_recording_id");

            migrationBuilder.CreateIndex(
                name: "ix_cover_versions_original_recording_id_cover_recording_id",
                table: "cover_versions",
                columns: new[] { "original_recording_id", "cover_recording_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recordings_mbid",
                table: "recordings",
                column: "mbid");

            migrationBuilder.CreateIndex(
                name: "ix_recordings_release_id_position",
                table: "recordings",
                columns: new[] { "release_id", "position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cover_versions");

            migrationBuilder.DropTable(
                name: "recordings");
        }
    }
}

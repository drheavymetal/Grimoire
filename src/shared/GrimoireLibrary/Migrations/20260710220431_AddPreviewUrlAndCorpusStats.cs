using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Grimoire.Library.Migrations
{
    /// <inheritdoc />
    public partial class AddPreviewUrlAndCorpusStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preview_url",
                table: "artists",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "corpus_stats",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    mean_embedding = table.Column<Vector>(type: "vector(768)", nullable: true),
                    artist_count = table.Column<int>(type: "integer", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_corpus_stats", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "corpus_stats");

            migrationBuilder.DropColumn(
                name: "preview_url",
                table: "artists");
        }
    }
}

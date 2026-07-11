using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Library.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkComposer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "composer_id",
                table: "works",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_works_composer_id",
                table: "works",
                column: "composer_id");

            migrationBuilder.AddForeignKey(
                name: "fk_works_artists_composer_id",
                table: "works",
                column: "composer_id",
                principalTable: "artists",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_works_artists_composer_id",
                table: "works");

            migrationBuilder.DropIndex(
                name: "ix_works_composer_id",
                table: "works");

            migrationBuilder.DropColumn(
                name: "composer_id",
                table: "works");
        }
    }
}

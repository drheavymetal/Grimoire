using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Grimoire.Library.Migrations
{
    /// <inheritdoc />
    public partial class AddGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "verdict_game_opt_in",
                table: "AspNetUsers",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "games",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opponent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    difficulty = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_games", x => x.id);
                    table.ForeignKey(
                        name: "fk_games_asp_net_users_opponent_id",
                        column: x => x.opponent_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_games_asp_net_users_player_id",
                        column: x => x.player_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_rounds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    artist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    truth = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    answer = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    correct = table.Column<bool>(type: "boolean", nullable: true),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_game_rounds", x => x.id);
                    table.ForeignKey(
                        name: "fk_game_rounds_artists_artist_id",
                        column: x => x.artist_id,
                        principalTable: "artists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_game_rounds_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_game_rounds_artist_id",
                table: "game_rounds",
                column: "artist_id");

            migrationBuilder.CreateIndex(
                name: "ix_game_rounds_game_id_ordinal",
                table: "game_rounds",
                columns: new[] { "game_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_games_opponent_id_created_at",
                table: "games",
                columns: new[] { "opponent_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_games_player_id_created_at",
                table: "games",
                columns: new[] { "player_id", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_rounds");

            migrationBuilder.DropTable(
                name: "games");

            migrationBuilder.DropColumn(
                name: "verdict_game_opt_in",
                table: "AspNetUsers");
        }
    }
}

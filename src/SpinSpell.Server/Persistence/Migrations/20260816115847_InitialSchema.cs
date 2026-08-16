using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpinSpell.Server.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rooms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    state = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rooms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "game_results",
                columns: table => new
                {
                    room_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_count = table.Column<int>(type: "integer", nullable: false),
                    rounds_played = table.Column<int>(type: "integer", nullable: false),
                    skips = table.Column<int>(type: "integer", nullable: false),
                    questions_submitted = table.Column<int>(type: "integer", nullable: false),
                    seconds_to_first_challenge = table.Column<int>(type: "integer", nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_results", x => x.room_id);
                    table.ForeignKey(
                        name: "FK_game_results_rooms_room_id",
                        column: x => x.room_id,
                        principalTable: "rooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rooms_code_active",
                table: "rooms",
                column: "code",
                unique: true,
                filter: "status = 'active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_results");

            migrationBuilder.DropTable(
                name: "rooms");
        }
    }
}

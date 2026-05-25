using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MLE_Infobot.Migrations
{
    /// <inheritdoc />
#pragma warning disable CS8981 // The type name only contains lower-cased ascii characters. Such names may become reserved for the language.
#pragma warning disable IDE1006 // Naming Styles
    public partial class primarykeytest : Migration
#pragma warning restore IDE1006 // Naming Styles
#pragma warning restore CS8981 // The type name only contains lower-cased ascii characters. Such names may become reserved for the language.
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlayoffWeekId",
                table: "Week",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeasonWeekId",
                table: "Week",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MappingVal",
                columns: table => new
                {
                    MappingValId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WeekId = table.Column<int>(type: "INTEGER", nullable: false),
                    MappingValue = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingVal", x => x.MappingValId);
                    table.ForeignKey(
                        name: "FK_MappingVal_Week_WeekId",
                        column: x => x.WeekId,
                        principalTable: "Week",
                        principalColumn: "WeekId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerSquadPlayer",
                columns: table => new
                {
                    PlayerSquadPlayerId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SquadPlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    SquadId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerID = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerSquadPlayer", x => x.PlayerSquadPlayerId);
                    table.ForeignKey(
                        name: "FK_PlayerSquadPlayer_Squad_SquadId",
                        column: x => x.SquadId,
                        principalTable: "Squad",
                        principalColumn: "SquadId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubstituteSquadPlayer",
                columns: table => new
                {
                    SubstituteSquadPlayerId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SquadPlayerId = table.Column<int>(type: "INTEGER", nullable: false),
                    SquadId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerID = table.Column<ulong>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubstituteSquadPlayer", x => x.SubstituteSquadPlayerId);
                    table.ForeignKey(
                        name: "FK_SubstituteSquadPlayer_Squad_SquadId",
                        column: x => x.SquadId,
                        principalTable: "Squad",
                        principalColumn: "SquadId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MappingVal_WeekId",
                table: "MappingVal",
                column: "WeekId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSquadPlayer_SquadId",
                table: "PlayerSquadPlayer",
                column: "SquadId");

            migrationBuilder.CreateIndex(
                name: "IX_SubstituteSquadPlayer_SquadId",
                table: "SubstituteSquadPlayer",
                column: "SquadId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MappingVal");

            migrationBuilder.DropTable(
                name: "PlayerSquadPlayer");

            migrationBuilder.DropTable(
                name: "SubstituteSquadPlayer");

            migrationBuilder.DropColumn(
                name: "PlayoffWeekId",
                table: "Week");

            migrationBuilder.DropColumn(
                name: "SeasonWeekId",
                table: "Week");
        }
    }
}

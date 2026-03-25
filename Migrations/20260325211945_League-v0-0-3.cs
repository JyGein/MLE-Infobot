using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MLE_Infobot.Migrations
{
    /// <inheritdoc />
    public partial class Leaguev003 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "Week",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SquadNumber",
                table: "Squad",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "State",
                table: "Week");

            migrationBuilder.DropColumn(
                name: "SquadNumber",
                table: "Squad");
        }
    }
}

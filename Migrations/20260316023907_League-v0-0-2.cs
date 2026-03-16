using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MLE_Infobot.Migrations
{
    /// <inheritdoc />
    public partial class Leaguev002 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "Seasons",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Winner",
                table: "Match",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "State",
                table: "Seasons");

            migrationBuilder.DropColumn(
                name: "Winner",
                table: "Match");
        }
    }
}

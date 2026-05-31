using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MLE_Infobot.Migrations
{
    /// <inheritdoc />
    public partial class GameWithState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "Game",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "State",
                table: "Game");
        }
    }
}

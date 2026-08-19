using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MLE_Infobot.Migrations
{
    /// <inheritdoc />
    public partial class SeededSquads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlayoffSeed",
                table: "Squad",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlayoffSeed",
                table: "Squad");
        }
    }
}

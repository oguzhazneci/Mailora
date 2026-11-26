using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MYAcademy_IdentityEmailProject.Migrations
{
    /// <inheritdoc />
    public partial class AddValidCodeAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ValidCodeAttempts",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValidCodeAttempts",
                table: "AspNetUsers");
        }
    }
}

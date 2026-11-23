using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MYAcademy_IdentityEmailProject.Migrations
{
    /// <inheritdoc />
    public partial class AddArchivedDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedDate",
                table: "MailboxItems",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedDate",
                table: "MailboxItems");
        }
    }
}

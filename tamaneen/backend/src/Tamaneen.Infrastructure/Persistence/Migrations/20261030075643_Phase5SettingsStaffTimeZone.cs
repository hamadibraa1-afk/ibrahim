using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5SettingsStaffTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                schema: "identity",
                table: "StaffProfiles",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: false,
                defaultValue: "Asia/Riyadh");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZone",
                schema: "identity",
                table: "StaffProfiles");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5ProfileLicenseDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "LicenseExpiresOn",
                schema: "clinical",
                table: "TherapistProfiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LicenseVerifiedAtUtc",
                schema: "clinical",
                table: "TherapistProfiles",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LicenseVerifiedByUserId",
                schema: "clinical",
                table: "TherapistProfiles",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LicenseExpiresOn",
                schema: "clinical",
                table: "TherapistProfiles");

            migrationBuilder.DropColumn(
                name: "LicenseVerifiedAtUtc",
                schema: "clinical",
                table: "TherapistProfiles");

            migrationBuilder.DropColumn(
                name: "LicenseVerifiedByUserId",
                schema: "clinical",
                table: "TherapistProfiles");
        }
    }
}

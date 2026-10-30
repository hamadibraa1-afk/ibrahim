using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5AppointmentsMissedCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MissedCheckIn",
                schema: "clinical",
                table: "Appointments",
                type: "varchar(8)",
                unicode: false,
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MissedCheckInAtUtc",
                schema: "clinical",
                table: "Appointments",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MissedCheckIn",
                schema: "clinical",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "MissedCheckInAtUtc",
                schema: "clinical",
                table: "Appointments");
        }
    }
}

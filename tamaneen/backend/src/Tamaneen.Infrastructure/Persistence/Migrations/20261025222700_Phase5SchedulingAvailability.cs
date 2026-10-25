using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5SchedulingAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "Source",
                schema: "clinical",
                table: "AvailabilitySlots",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<byte[]>(
                name: "TherapistCancelNoteCipher",
                schema: "clinical",
                table: "Appointments",
                type: "varbinary(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TherapistLateAtUtc",
                schema: "clinical",
                table: "Appointments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TherapistLateMinutes",
                schema: "clinical",
                table: "Appointments",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TherapistAvailabilities",
                schema: "clinical",
                columns: table => new
                {
                    TherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WeeklyJson = table.Column<string>(type: "varchar(4000)", unicode: false, maxLength: 4000, nullable: false),
                    ExceptionsJson = table.Column<string>(type: "varchar(8000)", unicode: false, maxLength: 8000, nullable: false),
                    BufferMinutes = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TherapistAvailabilities", x => x.TherapistUserId);
                    table.ForeignKey(
                        name: "FK_TherapistAvailabilities_TherapistProfiles_TherapistUserId",
                        column: x => x.TherapistUserId,
                        principalSchema: "clinical",
                        principalTable: "TherapistProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TherapistAvailabilities",
                schema: "clinical");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "clinical",
                table: "AvailabilitySlots");

            migrationBuilder.DropColumn(
                name: "TherapistCancelNoteCipher",
                schema: "clinical",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "TherapistLateAtUtc",
                schema: "clinical",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "TherapistLateMinutes",
                schema: "clinical",
                table: "Appointments");
        }
    }
}

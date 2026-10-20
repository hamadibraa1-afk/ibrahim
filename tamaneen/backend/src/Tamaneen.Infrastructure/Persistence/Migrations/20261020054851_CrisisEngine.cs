using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrisisEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CrisisEvents_PatientId",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ContactDeadlineUtc",
                schema: "crisis",
                table: "CrisisEvents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "EscalationTier",
                schema: "crisis",
                table: "CrisisEvents",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "crisis",
                table: "CrisisEvents",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TriggerRuleIds",
                schema: "crisis",
                table: "CrisisEvents",
                type: "varchar(1000)",
                unicode: false,
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ChatLocks",
                schema: "crisis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrisisEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LockedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatLocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatLocks_CrisisEvents_CrisisEventId",
                        column: x => x.CrisisEventId,
                        principalSchema: "crisis",
                        principalTable: "CrisisEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatLocks_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PageRequests",
                schema: "crisis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrisisEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tier = table.Column<byte>(type: "tinyint", nullable: false),
                    PseudonymId = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                    Level = table.Column<byte>(type: "tinyint", nullable: false),
                    PatientLanguage = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageRequests_CrisisEvents_CrisisEventId",
                        column: x => x.CrisisEventId,
                        principalSchema: "crisis",
                        principalTable: "CrisisEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_CrisisEvents_OneOpenPerPatient",
                schema: "crisis",
                table: "CrisisEvents",
                column: "PatientId",
                unique: true,
                filter: "[Status] <> 2");

            migrationBuilder.CreateIndex(
                name: "IX_ChatLocks_CrisisEventId",
                schema: "crisis",
                table: "ChatLocks",
                column: "CrisisEventId");

            migrationBuilder.CreateIndex(
                name: "UX_ChatLocks_OneActivePerPatient",
                schema: "crisis",
                table: "ChatLocks",
                column: "PatientId",
                unique: true,
                filter: "[ReleasedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PageRequests_CrisisEventId",
                schema: "crisis",
                table: "PageRequests",
                column: "CrisisEventId");

            migrationBuilder.CreateIndex(
                name: "IX_PageRequests_Due",
                schema: "crisis",
                table: "PageRequests",
                columns: new[] { "SentAtUtc", "NextAttemptAtUtc" });

            // Chat locks carry a PatientId, so they fall under the same row-level security as other patient data.
            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[ChatLocks],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[ChatLocks] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[ChatLocks] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[ChatLocks] BEFORE DELETE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    DROP FILTER PREDICATE ON [crisis].[ChatLocks],
    DROP BLOCK PREDICATE ON [crisis].[ChatLocks] AFTER INSERT,
    DROP BLOCK PREDICATE ON [crisis].[ChatLocks] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [crisis].[ChatLocks] BEFORE DELETE;");

            migrationBuilder.DropTable(
                name: "ChatLocks",
                schema: "crisis");

            migrationBuilder.DropTable(
                name: "PageRequests",
                schema: "crisis");

            migrationBuilder.DropIndex(
                name: "UX_CrisisEvents_OneOpenPerPatient",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "ContactDeadlineUtc",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "EscalationTier",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "TriggerRuleIds",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.CreateIndex(
                name: "IX_CrisisEvents_PatientId",
                schema: "crisis",
                table: "CrisisEvents",
                column: "PatientId");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <summary>P56 waitlist entries, one active per patient, under row-level security.</summary>
    public partial class PatientWaitlistEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WaitlistEntries",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Gender = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    Language = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    Times = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Days = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    TimeZone = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    RiskLevel = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    JoinedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NotifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NotifiedSlotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeftAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaitlistEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_TherapistProfiles_TherapistUserId",
                        column: x => x.TherapistUserId,
                        principalSchema: "clinical",
                        principalTable: "TherapistProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WaitlistEntries_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_Status_RiskLevel_JoinedAtUtc",
                schema: "clinical",
                table: "WaitlistEntries",
                columns: new[] { "Status", "RiskLevel", "JoinedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_TherapistUserId",
                schema: "clinical",
                table: "WaitlistEntries",
                column: "TherapistUserId");

            migrationBuilder.CreateIndex(
                name: "UX_WaitlistEntries_OneActive",
                schema: "clinical",
                table: "WaitlistEntries",
                column: "PatientId",
                unique: true,
                filter: "[Status] <> 2");

            // The patient and clinical staff as for every patient table; the scheduling service also matches the
            // whole queue against freed times (it reads and marks entries, nothing else).
            migrationBuilder.Sql(@"
CREATE FUNCTION [security].[fn_waitlist_access](@PatientId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'SchedulingService'
    UNION ALL
    SELECT [allowed] FROM [security].[fn_patient_access](@PatientId);");

            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    ADD FILTER PREDICATE [security].[fn_waitlist_access]([PatientId]) ON [clinical].[WaitlistEntries],
    ADD BLOCK  PREDICATE [security].[fn_waitlist_access]([PatientId]) ON [clinical].[WaitlistEntries] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_waitlist_access]([PatientId]) ON [clinical].[WaitlistEntries] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_waitlist_access]([PatientId]) ON [clinical].[WaitlistEntries] BEFORE DELETE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    DROP FILTER PREDICATE ON [clinical].[WaitlistEntries],
    DROP BLOCK PREDICATE ON [clinical].[WaitlistEntries] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[WaitlistEntries] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[WaitlistEntries] BEFORE DELETE;");
            migrationBuilder.Sql("DROP FUNCTION [security].[fn_waitlist_access];");

            migrationBuilder.DropTable(
                name: "WaitlistEntries",
                schema: "clinical");
        }
    }
}

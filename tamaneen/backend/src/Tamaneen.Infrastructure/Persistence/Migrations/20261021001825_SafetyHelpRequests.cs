using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Help-now idempotency keys (PB-26, EC-04): one row per press, so a double tap or a queued resend joins the
    /// first request instead of reaching the crisis gate again. Rows carry a PatientId, so row-level security applies.
    /// </summary>
    public partial class SafetyHelpRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HelpRequests",
                schema: "crisis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrisisEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HelpRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HelpRequests_CrisisEvents_CrisisEventId",
                        column: x => x.CrisisEventId,
                        principalSchema: "crisis",
                        principalTable: "CrisisEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HelpRequests_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HelpRequests_CrisisEventId",
                schema: "crisis",
                table: "HelpRequests",
                column: "CrisisEventId");

            migrationBuilder.CreateIndex(
                name: "UX_HelpRequests_PatientKey",
                schema: "crisis",
                table: "HelpRequests",
                columns: new[] { "PatientId", "RequestKey" },
                unique: true);

            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[HelpRequests],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[HelpRequests] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[HelpRequests] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[HelpRequests] BEFORE DELETE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    DROP FILTER PREDICATE ON [crisis].[HelpRequests],
    DROP BLOCK PREDICATE ON [crisis].[HelpRequests] AFTER INSERT,
    DROP BLOCK PREDICATE ON [crisis].[HelpRequests] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [crisis].[HelpRequests] BEFORE DELETE;");

            migrationBuilder.DropTable(
                name: "HelpRequests",
                schema: "crisis");
        }
    }
}

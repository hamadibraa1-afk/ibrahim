using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <summary>S57 / §10.4: the patient's request to continue care, under row-level security.</summary>
    public partial class Phase5ClosureContinueRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CareContinueRequests",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    ClosureId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareContinueRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareContinueRequests_CareClosures_ClosureId",
                        column: x => x.ClosureId,
                        principalSchema: "clinical",
                        principalTable: "CareClosures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareContinueRequests_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareContinueRequests_ClosureId",
                schema: "clinical",
                table: "CareContinueRequests",
                column: "ClosureId");

            migrationBuilder.CreateIndex(
                name: "IX_CareContinueRequests_PatientId_WithdrawnAtUtc",
                schema: "clinical",
                table: "CareContinueRequests",
                columns: new[] { "PatientId", "WithdrawnAtUtc" });

            // The patient's own rows, the assigned therapist, supervisors and the lead: the same rule as CareClosures.
            migrationBuilder.Sql("ALTER SECURITY POLICY [security].[PatientDataPolicy]\n" + string.Join(",\n",
                "    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[CareContinueRequests]",
                "    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[CareContinueRequests] AFTER INSERT",
                "    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[CareContinueRequests] AFTER UPDATE",
                "    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[CareContinueRequests] BEFORE DELETE") + ";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY [security].[PatientDataPolicy]\n" + string.Join(",\n",
                "    DROP FILTER PREDICATE ON [clinical].[CareContinueRequests]",
                "    DROP BLOCK PREDICATE ON [clinical].[CareContinueRequests] AFTER INSERT",
                "    DROP BLOCK PREDICATE ON [clinical].[CareContinueRequests] AFTER UPDATE",
                "    DROP BLOCK PREDICATE ON [clinical].[CareContinueRequests] BEFORE DELETE") + ";");
            migrationBuilder.DropTable(
                name: "CareContinueRequests",
                schema: "clinical");
        }
    }
}

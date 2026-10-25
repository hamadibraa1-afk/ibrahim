using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Idempotency keys for the patient writes the outbox queues offline (P66, EC-01, EC-07): one row per key, with
    /// the first response encrypted under the patient's key. Rows carry a PatientId, so row-level security applies.
    /// </summary>
    public partial class OutboxIdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Method = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    Path = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false),
                    ReservedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    ResponseCipher = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdempotencyRecords_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_IdempotencyRecords_PatientKey",
                schema: "clinical",
                table: "IdempotencyRecords",
                columns: new[] { "PatientId", "Key" },
                unique: true);

            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[IdempotencyRecords],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[IdempotencyRecords] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[IdempotencyRecords] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[IdempotencyRecords] BEFORE DELETE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    DROP FILTER PREDICATE ON [clinical].[IdempotencyRecords],
    DROP BLOCK PREDICATE ON [clinical].[IdempotencyRecords] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[IdempotencyRecords] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[IdempotencyRecords] BEFORE DELETE;");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "clinical");
        }
    }
}

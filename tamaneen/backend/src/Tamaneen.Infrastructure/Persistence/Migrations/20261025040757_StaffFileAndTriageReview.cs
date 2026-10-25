using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// S02 and S03: the therapist's file review (dismissals, edits, usefulness rating) under the same row-level security
    /// as the rest of the patient's file, and the triage claim, co-sign and referral columns. Cases created before this
    /// migration keep RuleRiskLevel 0, which the API reads as "same as the current level".
    /// </summary>
    public partial class StaffFileAndTriageReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClaimedByUserId",
                schema: "clinical",
                table: "TriageCases",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClaimedUntilUtc",
                schema: "clinical",
                table: "TriageCases",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "PendingRiskLevel",
                schema: "clinical",
                table: "TriageCases",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PendingRiskRequestedAtUtc",
                schema: "clinical",
                table: "TriageCases",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PendingRiskRequestedBy",
                schema: "clinical",
                table: "TriageCases",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Referrals",
                schema: "clinical",
                table: "TriageCases",
                type: "varchar(200)",
                unicode: false,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "clinical",
                table: "TriageCases",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte>(
                name: "RuleRiskLevel",
                schema: "clinical",
                table: "TriageCases",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.CreateTable(
                name: "FileReviews",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TriageCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EditsCipher = table.Column<byte[]>(type: "varbinary(max)", maxLength: 16000, nullable: true),
                    DismissedKeys = table.Column<string>(type: "varchar(400)", unicode: false, maxLength: 400, nullable: false),
                    Rating = table.Column<byte>(type: "tinyint", nullable: true),
                    RatingCommentCipher = table.Column<byte[]>(type: "varbinary(2000)", maxLength: 2000, nullable: true),
                    RatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileReviews_TriageCases_TriageCaseId",
                        column: x => x.TriageCaseId,
                        principalSchema: "clinical",
                        principalTable: "TriageCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FileReviews_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileReviews_PatientId",
                schema: "clinical",
                table: "FileReviews",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_FileReviews_TriageCaseId",
                schema: "clinical",
                table: "FileReviews",
                column: "TriageCaseId",
                unique: true);

            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[FileReviews],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[FileReviews] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[FileReviews] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[FileReviews] BEFORE DELETE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    DROP FILTER PREDICATE ON [clinical].[FileReviews],
    DROP BLOCK PREDICATE ON [clinical].[FileReviews] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[FileReviews] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[FileReviews] BEFORE DELETE;");

            migrationBuilder.DropTable(
                name: "FileReviews",
                schema: "clinical");

            migrationBuilder.DropColumn(
                name: "ClaimedByUserId",
                schema: "clinical",
                table: "TriageCases");

            migrationBuilder.DropColumn(
                name: "ClaimedUntilUtc",
                schema: "clinical",
                table: "TriageCases");

            migrationBuilder.DropColumn(
                name: "PendingRiskLevel",
                schema: "clinical",
                table: "TriageCases");

            migrationBuilder.DropColumn(
                name: "PendingRiskRequestedAtUtc",
                schema: "clinical",
                table: "TriageCases");

            migrationBuilder.DropColumn(
                name: "PendingRiskRequestedBy",
                schema: "clinical",
                table: "TriageCases");

            migrationBuilder.DropColumn(
                name: "Referrals",
                schema: "clinical",
                table: "TriageCases");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "clinical",
                table: "TriageCases");

            migrationBuilder.DropColumn(
                name: "RuleRiskLevel",
                schema: "clinical",
                table: "TriageCases");
        }
    }
}

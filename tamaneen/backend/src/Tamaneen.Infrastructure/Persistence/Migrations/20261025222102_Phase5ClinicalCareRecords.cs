using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase5ClinicalCareRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                schema: "clinical",
                table: "SafetyPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<byte>(
                name: "GateLevel",
                schema: "clinical",
                table: "HomeworkCheckins",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAtUtc",
                schema: "clinical",
                table: "HomeworkAssignments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateCode",
                schema: "clinical",
                table: "HomeworkAssignments",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAtUtc",
                schema: "clinical",
                table: "HomeworkAssignments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CareClosures",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    SessionCount = table.Column<int>(type: "int", nullable: false),
                    DurationWeeks = table.Column<int>(type: "int", nullable: false),
                    PatientSummaryCipher = table.Column<byte[]>(type: "varbinary(max)", maxLength: 8100, nullable: true),
                    RelapsePlanCipher = table.Column<byte[]>(type: "varbinary(4000)", maxLength: 4000, nullable: true),
                    FollowUp = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    KeepLibrary = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DecidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectionCipher = table.Column<byte[]>(type: "varbinary(4000)", maxLength: 4000, nullable: true),
                    ReturnRequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareClosures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareClosures_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChangeTherapistRequests",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromTherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    TriageCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    NewTherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeTherapistRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeTherapistRequests_TriageCases_TriageCaseId",
                        column: x => x.TriageCaseId,
                        principalSchema: "clinical",
                        principalTable: "TriageCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChangeTherapistRequests_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SafetyPlanVersions",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    SectionsCipher = table.Column<byte[]>(type: "varbinary(max)", maxLength: 16000, nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorRole = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafetyPlanVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SafetyPlanVersions_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionNotes",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Template = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    FieldsCipher = table.Column<byte[]>(type: "varbinary(max)", maxLength: 40000, nullable: false),
                    SummaryCipher = table.Column<byte[]>(type: "varbinary(4000)", maxLength: 4000, nullable: true),
                    ShareSummary = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionNotes_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "clinical",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionNotes_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionSummaries",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PointsCipher = table.Column<byte[]>(type: "varbinary(4000)", maxLength: 4000, nullable: false),
                    SharedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionSummaries_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "clinical",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionSummaries_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TreatmentPlanVersions",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ContentCipher = table.Column<byte[]>(type: "varbinary(max)", maxLength: 40000, nullable: false),
                    Frequency = table.Column<byte>(type: "tinyint", nullable: false),
                    ReviewOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentPlanVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TreatmentPlanVersions_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionNoteAddenda",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TextCipher = table.Column<byte[]>(type: "varbinary(max)", maxLength: 8100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionNoteAddenda", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionNoteAddenda_SessionNotes_NoteId",
                        column: x => x.NoteId,
                        principalSchema: "clinical",
                        principalTable: "SessionNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareClosures_PatientId_Status",
                schema: "clinical",
                table: "CareClosures",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CareClosures_Status_SubmittedAtUtc",
                schema: "clinical",
                table: "CareClosures",
                columns: new[] { "Status", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeTherapistRequests_TriageCaseId",
                schema: "clinical",
                table: "ChangeTherapistRequests",
                column: "TriageCaseId");

            migrationBuilder.CreateIndex(
                name: "UX_ChangeTherapistRequests_OneOpen",
                schema: "clinical",
                table: "ChangeTherapistRequests",
                column: "PatientId",
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SafetyPlanVersions_PatientId_Version",
                schema: "clinical",
                table: "SafetyPlanVersions",
                columns: new[] { "PatientId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionNoteAddenda_NoteId_CreatedAtUtc",
                schema: "clinical",
                table: "SessionNoteAddenda",
                columns: new[] { "NoteId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SessionNoteAddenda_PatientId",
                schema: "clinical",
                table: "SessionNoteAddenda",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionNotes_AppointmentId",
                schema: "clinical",
                table: "SessionNotes",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionNotes_PatientId",
                schema: "clinical",
                table: "SessionNotes",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionSummaries_AppointmentId",
                schema: "clinical",
                table: "SessionSummaries",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionSummaries_PatientId",
                schema: "clinical",
                table: "SessionSummaries",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanVersions_PatientId_Version",
                schema: "clinical",
                table: "TreatmentPlanVersions",
                columns: new[] { "PatientId", "Version" },
                unique: true);

            // Existing safety plans become version 1, with that version in the history. Row-level security would hide
            // the rows from the migration's connection, so the policy is paused for the backfill only.
            migrationBuilder.Sql("ALTER SECURITY POLICY [security].[PatientDataPolicy] WITH (STATE = OFF);");
            migrationBuilder.Sql(@"
UPDATE [clinical].[SafetyPlans] SET [Version] = 1 WHERE [Version] = 0;
INSERT INTO [clinical].[SafetyPlanVersions] ([Id], [PatientId], [Version], [SectionsCipher], [AuthorUserId], [AuthorRole], [CreatedAtUtc])
SELECT NEWID(), [PatientId], 1, [SectionsCipher], [UpdatedByUserId], [UpdatedByRole], [UpdatedAtUtc] FROM [clinical].[SafetyPlans];");
            migrationBuilder.Sql("ALTER SECURITY POLICY [security].[PatientDataPolicy] WITH (STATE = ON);");

            // Session notes and their addenda: the treating clinicians only. Never the patient, never the crisis service.
            migrationBuilder.Sql(@"
CREATE FUNCTION [security].[fn_clinician_only_access](@PatientId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) IN (N'Supervisor', N'ClinicalLead')
       OR (CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'Therapist'
           AND EXISTS (SELECT 1
                       FROM [clinical].[CareAssignments] AS ca
                       WHERE ca.[PatientId] = @PatientId
                         AND ca.[TherapistUserId] = CAST(SESSION_CONTEXT(N'user_id') AS uniqueidentifier)
                         AND ca.[EndedAtUtc] IS NULL));");

            // Change-of-therapist requests (P44): the patient, supervisors, the clinical lead and the scheduling service
            // (which ends the old assignment at the new booking). The current therapist never sees the request or its reason.
            migrationBuilder.Sql(@"
CREATE FUNCTION [security].[fn_change_request_access](@PatientId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) IN (N'Supervisor', N'ClinicalLead', N'SchedulingService')
       OR (CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'Patient'
           AND @PatientId = CAST(SESSION_CONTEXT(N'patient_id') AS uniqueidentifier));");

            migrationBuilder.Sql("ALTER SECURITY POLICY [security].[PatientDataPolicy]\n" + string.Join(",\n",
                Predicates("[clinical].[TreatmentPlanVersions]", "fn_patient_access"),
                Predicates("[clinical].[SafetyPlanVersions]", "fn_patient_access"),
                Predicates("[clinical].[SessionSummaries]", "fn_patient_access"),
                Predicates("[clinical].[CareClosures]", "fn_patient_access"),
                Predicates("[clinical].[SessionNotes]", "fn_clinician_only_access"),
                Predicates("[clinical].[SessionNoteAddenda]", "fn_clinician_only_access"),
                Predicates("[clinical].[ChangeTherapistRequests]", "fn_change_request_access")) + ";");
        }

        private static readonly string[] Tables =
        [
            "[clinical].[TreatmentPlanVersions]", "[clinical].[SafetyPlanVersions]", "[clinical].[SessionSummaries]", "[clinical].[CareClosures]",
            "[clinical].[SessionNotes]", "[clinical].[SessionNoteAddenda]", "[clinical].[ChangeTherapistRequests]",
        ];

        private static string Predicates(string table, string function) => string.Join(",\n",
            $"    ADD FILTER PREDICATE [security].[{function}]([PatientId]) ON {table}",
            $"    ADD BLOCK  PREDICATE [security].[{function}]([PatientId]) ON {table} AFTER INSERT",
            $"    ADD BLOCK  PREDICATE [security].[{function}]([PatientId]) ON {table} AFTER UPDATE",
            $"    ADD BLOCK  PREDICATE [security].[{function}]([PatientId]) ON {table} BEFORE DELETE");

        private static string DropPredicates(string table) => string.Join(",\n",
            $"    DROP FILTER PREDICATE ON {table}",
            $"    DROP BLOCK PREDICATE ON {table} AFTER INSERT",
            $"    DROP BLOCK PREDICATE ON {table} AFTER UPDATE",
            $"    DROP BLOCK PREDICATE ON {table} BEFORE DELETE");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER SECURITY POLICY [security].[PatientDataPolicy]\n" + string.Join(",\n", Tables.Select(DropPredicates)) + ";");
            migrationBuilder.Sql("DROP FUNCTION [security].[fn_change_request_access];");
            migrationBuilder.Sql("DROP FUNCTION [security].[fn_clinician_only_access];");
            migrationBuilder.DropTable(
                name: "CareClosures",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "ChangeTherapistRequests",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "SafetyPlanVersions",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "SessionNoteAddenda",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "SessionSummaries",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "TreatmentPlanVersions",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "SessionNotes",
                schema: "clinical");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "clinical",
                table: "SafetyPlans");

            migrationBuilder.DropColumn(
                name: "GateLevel",
                schema: "clinical",
                table: "HomeworkCheckins");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                schema: "clinical",
                table: "HomeworkAssignments");

            migrationBuilder.DropColumn(
                name: "TemplateCode",
                schema: "clinical",
                table: "HomeworkAssignments");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                schema: "clinical",
                table: "HomeworkAssignments");
        }
    }
}

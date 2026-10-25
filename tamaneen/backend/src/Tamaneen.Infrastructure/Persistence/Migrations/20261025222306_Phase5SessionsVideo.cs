using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <summary>Session room tables (presence, pre-session note, private feedback) under row-level security.</summary>
    public partial class Phase5SessionsVideo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionFeedback",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rating = table.Column<byte>(type: "tinyint", nullable: true),
                    Comfortable = table.Column<bool>(type: "bit", nullable: false),
                    CommentCipher = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionFeedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionFeedback_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "clinical",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionFeedback_TherapistProfiles_TherapistUserId",
                        column: x => x.TherapistUserId,
                        principalSchema: "clinical",
                        principalTable: "TherapistProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionFeedback_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionPrep",
                schema: "clinical",
                columns: table => new
                {
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NoteCipher = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionPrep", x => x.AppointmentId);
                    table.ForeignKey(
                        name: "FK_SessionPrep_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "clinical",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionPrep_TherapistProfiles_TherapistUserId",
                        column: x => x.TherapistUserId,
                        principalSchema: "clinical",
                        principalTable: "TherapistProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionPrep_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SessionPresence",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Participant = table.Column<byte>(type: "tinyint", nullable: false),
                    FirstJoinedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LeftAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionPresence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionPresence_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalSchema: "clinical",
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionPresence_TherapistProfiles_TherapistUserId",
                        column: x => x.TherapistUserId,
                        principalSchema: "clinical",
                        principalTable: "TherapistProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SessionPresence_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionFeedback_PatientId",
                schema: "clinical",
                table: "SessionFeedback",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionFeedback_TherapistUserId_CreatedAtUtc",
                schema: "clinical",
                table: "SessionFeedback",
                columns: new[] { "TherapistUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_SessionFeedback_Appointment",
                schema: "clinical",
                table: "SessionFeedback",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionPrep_PatientId",
                schema: "clinical",
                table: "SessionPrep",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionPrep_TherapistUserId",
                schema: "clinical",
                table: "SessionPrep",
                column: "TherapistUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionPresence_PatientId",
                schema: "clinical",
                table: "SessionPresence",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionPresence_TherapistUserId",
                schema: "clinical",
                table: "SessionPresence",
                column: "TherapistUserId");

            migrationBuilder.CreateIndex(
                name: "UX_SessionPresence_AppointmentParticipant",
                schema: "clinical",
                table: "SessionPresence",
                columns: new[] { "AppointmentId", "Participant" },
                unique: true);

            // Row-level security. Presence and the pre-session note follow the appointment: the patient and the
            // session's own therapist (plus supervisors, the lead and the crisis service). Feedback (P43) reaches
            // supervisors only: the patient and supervisors/the lead, never a therapist.
            migrationBuilder.Sql(@"
CREATE FUNCTION [security].[fn_session_feedback_access](@PatientId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) IN (N'Supervisor', N'ClinicalLead')
       OR (CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'Patient'
           AND @PatientId = CAST(SESSION_CONTEXT(N'patient_id') AS uniqueidentifier));");

            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    ADD FILTER PREDICATE [security].[fn_appointment_access]([PatientId], [TherapistUserId]) ON [clinical].[SessionPresence],
    ADD BLOCK  PREDICATE [security].[fn_appointment_access]([PatientId], [TherapistUserId]) ON [clinical].[SessionPresence] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_appointment_access]([PatientId], [TherapistUserId]) ON [clinical].[SessionPresence] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_appointment_access]([PatientId], [TherapistUserId]) ON [clinical].[SessionPresence] BEFORE DELETE,
    ADD FILTER PREDICATE [security].[fn_appointment_access]([PatientId], [TherapistUserId]) ON [clinical].[SessionPrep],
    ADD BLOCK  PREDICATE [security].[fn_appointment_access]([PatientId], [TherapistUserId]) ON [clinical].[SessionPrep] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_appointment_access]([PatientId], [TherapistUserId]) ON [clinical].[SessionPrep] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_appointment_access]([PatientId], [TherapistUserId]) ON [clinical].[SessionPrep] BEFORE DELETE,
    ADD FILTER PREDICATE [security].[fn_session_feedback_access]([PatientId]) ON [clinical].[SessionFeedback],
    ADD BLOCK  PREDICATE [security].[fn_session_feedback_access]([PatientId]) ON [clinical].[SessionFeedback] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_session_feedback_access]([PatientId]) ON [clinical].[SessionFeedback] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_session_feedback_access]([PatientId]) ON [clinical].[SessionFeedback] BEFORE DELETE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    DROP FILTER PREDICATE ON [clinical].[SessionPresence],
    DROP BLOCK PREDICATE ON [clinical].[SessionPresence] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[SessionPresence] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[SessionPresence] BEFORE DELETE,
    DROP FILTER PREDICATE ON [clinical].[SessionPrep],
    DROP BLOCK PREDICATE ON [clinical].[SessionPrep] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[SessionPrep] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[SessionPrep] BEFORE DELETE,
    DROP FILTER PREDICATE ON [clinical].[SessionFeedback],
    DROP BLOCK PREDICATE ON [clinical].[SessionFeedback] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[SessionFeedback] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[SessionFeedback] BEFORE DELETE;");
            migrationBuilder.Sql("DROP FUNCTION [security].[fn_session_feedback_access];");

            migrationBuilder.DropTable(
                name: "SessionFeedback",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "SessionPrep",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "SessionPresence",
                schema: "clinical");
        }
    }
}

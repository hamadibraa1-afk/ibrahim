using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The in-app inbox (P46, S41) under its own row-level security policy on RecipientUserId, the staff switches of
    /// S32, and read access for the notification service to upcoming appointments and closed crisis events
    /// (reminders, P38 follow-ups). Only the FILTER predicates of those two tables change: the service can read
    /// them, never write them, and the existing functions stay as they are.
    /// </summary>
    public partial class Phase5NotificationsInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StaffBookings",
                schema: "identity",
                table: "NotificationPreferences",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "StaffDailyDigest",
                schema: "identity",
                table: "NotificationPreferences",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "StaffWeeklyReport",
                schema: "identity",
                table: "NotificationPreferences",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    DataJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Link = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DedupeKey = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    External = table.Column<byte>(type: "tinyint", nullable: false),
                    ExternalChannel = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: true),
                    ExternalNotBeforeUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExternalAttempts = table.Column<byte>(type: "tinyint", nullable: false),
                    ExternalSentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_UserAccounts_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_External_ExternalNotBeforeUtc",
                schema: "identity",
                table: "Notifications",
                columns: new[] { "External", "ExternalNotBeforeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_CreatedAtUtc",
                schema: "identity",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Notifications_Dedupe",
                schema: "identity",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "DedupeKey" },
                unique: true,
                filter: "[DedupeKey] IS NOT NULL");

            // A person reads and changes only their own inbox; the notification service writes and dispatches.
            migrationBuilder.Sql(@"
CREATE FUNCTION [security].[fn_notification_access](@RecipientUserId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'NotificationService'
       OR (SESSION_CONTEXT(N'user_id') IS NOT NULL
           AND @RecipientUserId = CAST(SESSION_CONTEXT(N'user_id') AS uniqueidentifier)
           AND CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) IN (N'Patient', N'Therapist', N'Supervisor', N'ClinicalLead', N'Admin'));");

            migrationBuilder.Sql(@"
CREATE SECURITY POLICY [security].[NotificationPolicy]
    ADD FILTER PREDICATE [security].[fn_notification_access]([RecipientUserId]) ON [identity].[Notifications],
    ADD BLOCK  PREDICATE [security].[fn_notification_access]([RecipientUserId]) ON [identity].[Notifications] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_notification_access]([RecipientUserId]) ON [identity].[Notifications] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_notification_access]([RecipientUserId]) ON [identity].[Notifications] BEFORE DELETE
WITH (STATE = ON);");

            migrationBuilder.Sql(@"
CREATE FUNCTION [security].[fn_appointment_read](@PatientId uniqueidentifier, @TherapistUserId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'NotificationService'
    UNION ALL
    SELECT [allowed] FROM [security].[fn_appointment_access](@PatientId, @TherapistUserId);");

            migrationBuilder.Sql(@"
CREATE FUNCTION [security].[fn_crisis_read](@PatientId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'NotificationService'
    UNION ALL
    SELECT [allowed] FROM [security].[fn_crisis_access](@PatientId);");

            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    DROP FILTER PREDICATE ON [clinical].[Appointments],
    DROP FILTER PREDICATE ON [crisis].[CrisisEvents];");
            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    ADD FILTER PREDICATE [security].[fn_appointment_read]([PatientId], [TherapistUserId]) ON [clinical].[Appointments],
    ADD FILTER PREDICATE [security].[fn_crisis_read]([PatientId]) ON [crisis].[CrisisEvents];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    DROP FILTER PREDICATE ON [clinical].[Appointments],
    DROP FILTER PREDICATE ON [crisis].[CrisisEvents];");
            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    ADD FILTER PREDICATE [security].[fn_appointment_access]([PatientId], [TherapistUserId]) ON [clinical].[Appointments],
    ADD FILTER PREDICATE [security].[fn_crisis_access]([PatientId]) ON [crisis].[CrisisEvents];");
            migrationBuilder.Sql("DROP FUNCTION [security].[fn_appointment_read];");
            migrationBuilder.Sql("DROP FUNCTION [security].[fn_crisis_read];");
            migrationBuilder.Sql("DROP SECURITY POLICY [security].[NotificationPolicy];");
            migrationBuilder.Sql("DROP FUNCTION [security].[fn_notification_access];");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "StaffBookings",
                schema: "identity",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "StaffDailyDigest",
                schema: "identity",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "StaffWeeklyReport",
                schema: "identity",
                table: "NotificationPreferences");
        }
    }
}

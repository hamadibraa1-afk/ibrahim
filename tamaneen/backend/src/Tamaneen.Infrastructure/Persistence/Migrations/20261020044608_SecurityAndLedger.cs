using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Database-enforced protections that EF cannot model:
    /// 1. audit.AuditEntries as an append-only ledger table (tamper-evident, no UPDATE or DELETE possible).
    /// 2. Row-level security on every table that carries a PatientId. Predicates read the caller from
    ///    SESSION_CONTEXT (set by SessionContextInterceptor). An unset or unknown role sees no patient rows.
    ///    Admin is deliberately not granted: administrators manage the platform, not clinical content.
    /// </summary>
    public partial class SecurityAndLedger : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF SCHEMA_ID(N'audit') IS NULL EXEC(N'CREATE SCHEMA [audit]');");
            migrationBuilder.Sql("IF SCHEMA_ID(N'security') IS NULL EXEC(N'CREATE SCHEMA [security]');");

            migrationBuilder.Sql(@"
CREATE TABLE [audit].[AuditEntries]
(
    [Id]                 bigint IDENTITY(1,1) NOT NULL CONSTRAINT [PK_AuditEntries] PRIMARY KEY,
    [OccurredAtUtc]      datetimeoffset       NOT NULL,
    [ActorUserId]        uniqueidentifier     NULL,
    [ActorRole]          varchar(32)          NULL,
    [Action]             varchar(64)          NOT NULL,
    [EntityType]         varchar(64)          NOT NULL,
    [EntityId]           varchar(64)          NULL,
    [PatientPseudonymId] varchar(11)          NULL,
    [Reason]             nvarchar(1000)       NULL,
    [CorrelationId]      varchar(64)          NULL
)
WITH (LEDGER = ON (APPEND_ONLY = ON));");

            migrationBuilder.Sql(@"
CREATE FUNCTION [security].[fn_patient_access](@PatientId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) IN (N'Supervisor', N'ClinicalLead', N'CrisisService')
       OR (CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'Patient'
           AND @PatientId = CAST(SESSION_CONTEXT(N'patient_id') AS uniqueidentifier))
       OR (CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'Therapist'
           AND EXISTS (SELECT 1
                       FROM [clinical].[CareAssignments] AS ca
                       WHERE ca.[PatientId] = @PatientId
                         AND ca.[TherapistUserId] = CAST(SESSION_CONTEXT(N'user_id') AS uniqueidentifier)
                         AND ca.[EndedAtUtc] IS NULL));");

            // Identity and key tables additionally admit the data-protection service, which executes approved deletions.
            migrationBuilder.Sql(@"
CREATE FUNCTION [security].[fn_patient_vault_access](@PatientId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'DataProtectionService'
    UNION ALL
    SELECT [allowed] FROM [security].[fn_patient_access](@PatientId);");

            migrationBuilder.Sql(@"
CREATE FUNCTION [security].[fn_assignment_read](@PatientId uniqueidentifier, @TherapistUserId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) IN (N'Supervisor', N'ClinicalLead', N'CrisisService')
       OR (CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'Therapist'
           AND @TherapistUserId = CAST(SESSION_CONTEXT(N'user_id') AS uniqueidentifier))
       OR (CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'Patient'
           AND @PatientId = CAST(SESSION_CONTEXT(N'patient_id') AS uniqueidentifier));");

            // Only supervisors and the clinical lead create, change or end care assignments.
            migrationBuilder.Sql(@"
CREATE FUNCTION [security].[fn_assignment_write](@PatientId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) IN (N'Supervisor', N'ClinicalLead');");

            migrationBuilder.Sql(@"
CREATE SECURITY POLICY [security].[PatientDataPolicy]
    ADD FILTER PREDICATE [security].[fn_patient_vault_access]([PatientId]) ON [identity].[PatientProfiles],
    ADD BLOCK  PREDICATE [security].[fn_patient_vault_access]([PatientId]) ON [identity].[PatientProfiles] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_vault_access]([PatientId]) ON [identity].[PatientProfiles] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_vault_access]([PatientId]) ON [identity].[PatientProfiles] BEFORE DELETE,

    ADD FILTER PREDICATE [security].[fn_patient_vault_access]([PatientId]) ON [vault].[PatientKeys],
    ADD BLOCK  PREDICATE [security].[fn_patient_vault_access]([PatientId]) ON [vault].[PatientKeys] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_vault_access]([PatientId]) ON [vault].[PatientKeys] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_vault_access]([PatientId]) ON [vault].[PatientKeys] BEFORE DELETE,

    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[CrisisEvents],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[CrisisEvents] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[CrisisEvents] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[CrisisEvents] BEFORE DELETE,

    ADD FILTER PREDICATE [security].[fn_assignment_read]([PatientId], [TherapistUserId]) ON [clinical].[CareAssignments],
    ADD BLOCK  PREDICATE [security].[fn_assignment_write]([PatientId]) ON [clinical].[CareAssignments] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_assignment_write]([PatientId]) ON [clinical].[CareAssignments] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_assignment_write]([PatientId]) ON [clinical].[CareAssignments] BEFORE DELETE
WITH (STATE = ON, SCHEMABINDING = ON);");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY IF EXISTS [security].[PatientDataPolicy];");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS [security].[fn_assignment_write];");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS [security].[fn_assignment_read];");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS [security].[fn_patient_vault_access];");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS [security].[fn_patient_access];");

            // Dropping a ledger table keeps its history in sys.dropped_ledger_tables; the evidence survives.
            migrationBuilder.Sql("DROP TABLE IF EXISTS [audit].[AuditEntries];");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// On-call therapists are paged for every patient's crisis, so they must be able to read and act on it:
    /// crisis rows open to them for any patient, and the patient's profile and key (contact reveal, excerpt)
    /// only while that patient has an open crisis event.
    /// </summary>
    public partial class OnCallCrisisAccess : Migration
    {
        private const string CrisisTables = "[crisis].[CrisisEvents]|[crisis].[ChatLocks]|[crisis].[CrisisActions]";
        private const string VaultTables = "[identity].[PatientProfiles]|[vault].[PatientKeys]";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DropPredicates(CrisisTables + "|" + VaultTables));

            migrationBuilder.Sql(@"
CREATE FUNCTION [security].[fn_oncall]()
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'Therapist'
      AND EXISTS (SELECT 1
                  FROM [identity].[StaffProfiles] AS sp
                  WHERE sp.[UserId] = CAST(SESSION_CONTEXT(N'user_id') AS uniqueidentifier)
                    AND sp.[OnCallTier] IS NOT NULL);");

            migrationBuilder.Sql(@"
CREATE FUNCTION [security].[fn_crisis_access](@PatientId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT [allowed] FROM [security].[fn_patient_access](@PatientId)
    UNION ALL
    SELECT [allowed] FROM [security].[fn_oncall]();");

            migrationBuilder.Sql(@"
ALTER FUNCTION [security].[fn_patient_vault_access](@PatientId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'DataProtectionService'
    UNION ALL
    SELECT [allowed] FROM [security].[fn_patient_access](@PatientId)
    UNION ALL
    SELECT oc.[allowed]
    FROM [security].[fn_oncall]() AS oc
    WHERE EXISTS (SELECT 1 FROM [crisis].[CrisisEvents] AS ce WHERE ce.[PatientId] = @PatientId AND ce.[Status] <> 2);");

            migrationBuilder.Sql(AddPredicates(CrisisTables, "fn_crisis_access") + ",\n" + AddPredicates(VaultTables, "fn_patient_vault_access", alter: false));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DropPredicates(CrisisTables + "|" + VaultTables));

            migrationBuilder.Sql(@"
ALTER FUNCTION [security].[fn_patient_vault_access](@PatientId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'DataProtectionService'
    UNION ALL
    SELECT [allowed] FROM [security].[fn_patient_access](@PatientId);");

            migrationBuilder.Sql("DROP FUNCTION [security].[fn_crisis_access];");
            migrationBuilder.Sql("DROP FUNCTION [security].[fn_oncall];");

            migrationBuilder.Sql(AddPredicates(CrisisTables, "fn_patient_access") + ",\n" + AddPredicates(VaultTables, "fn_patient_vault_access", alter: false));
        }

        private static string DropPredicates(string tables) =>
            "ALTER SECURITY POLICY [security].[PatientDataPolicy]\n" + string.Join(",\n", tables.Split('|').SelectMany(t => new[]
            {
                $"    DROP FILTER PREDICATE ON {t}",
                $"    DROP BLOCK PREDICATE ON {t} AFTER INSERT",
                $"    DROP BLOCK PREDICATE ON {t} AFTER UPDATE",
                $"    DROP BLOCK PREDICATE ON {t} BEFORE DELETE",
            })) + ";";

        private static string AddPredicates(string tables, string function, bool alter = true) =>
            (alter ? "ALTER SECURITY POLICY [security].[PatientDataPolicy]\n" : string.Empty) + string.Join(",\n", tables.Split('|').SelectMany(t => new[]
            {
                $"    ADD FILTER PREDICATE [security].[{function}]([PatientId]) ON {t}",
                $"    ADD BLOCK  PREDICATE [security].[{function}]([PatientId]) ON {t} AFTER INSERT",
                $"    ADD BLOCK  PREDICATE [security].[{function}]([PatientId]) ON {t} AFTER UPDATE",
                $"    ADD BLOCK  PREDICATE [security].[{function}]([PatientId]) ON {t} BEFORE DELETE",
            }));
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Rebuilds the audit ledger with sequence-based ids. With IDENTITY, EF batches several inserts into one
    /// MERGE statement, which append-only ledger tables reject, so a second audit row in the same save failed.
    /// The previous table is dropped, which in SQL Server keeps it, its rows and its digests in
    /// sys.dropped_ledger_tables: no audit evidence is lost.
    /// </summary>
    public partial class AuditLedgerSequenceIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateSequence(
                name: "AuditEntryIds",
                schema: "audit",
                incrementBy: 10);

            migrationBuilder.Sql("DROP TABLE [audit].[AuditEntries];");
            migrationBuilder.Sql(@"
CREATE TABLE [audit].[AuditEntries]
(
    [Id]                 bigint           NOT NULL CONSTRAINT [PK_AuditEntries] PRIMARY KEY,
    [OccurredAtUtc]      datetimeoffset   NOT NULL,
    [ActorUserId]        uniqueidentifier NULL,
    [ActorRole]          varchar(32)      NULL,
    [Action]             varchar(64)      NOT NULL,
    [EntityType]         varchar(64)      NOT NULL,
    [EntityId]           varchar(64)      NULL,
    [PatientPseudonymId] varchar(11)      NULL,
    [Reason]             nvarchar(1000)   NULL,
    [CorrelationId]      varchar(64)      NULL
)
WITH (LEDGER = ON (APPEND_ONLY = ON));");
            migrationBuilder.Sql("CREATE INDEX [IX_AuditEntries_Entity] ON [audit].[AuditEntries] ([EntityType], [EntityId]);");
            migrationBuilder.Sql("CREATE INDEX [IX_AuditEntries_Pseudonym] ON [audit].[AuditEntries] ([PatientPseudonymId]) WHERE [PatientPseudonymId] IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Ledger history cannot be moved back; the table returns to its earlier shape, empty.
            migrationBuilder.Sql("DROP TABLE [audit].[AuditEntries];");
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

            migrationBuilder.DropSequence(
                name: "AuditEntryIds",
                schema: "audit");
        }
    }
}

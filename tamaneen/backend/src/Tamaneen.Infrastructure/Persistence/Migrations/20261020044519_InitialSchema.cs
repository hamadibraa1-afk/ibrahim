using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ai");

            migrationBuilder.EnsureSchema(
                name: "clinical");

            migrationBuilder.EnsureSchema(
                name: "crisis");

            migrationBuilder.EnsureSchema(
                name: "vault");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "AiCallRecords",
                schema: "ai",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PseudonymId = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                    Purpose = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    PromptVersion = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    ModelId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    Language = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    Outcome = table.Column<byte>(type: "tinyint", nullable: false),
                    LatencyMs = table.Column<int>(type: "int", nullable: false),
                    InputTokens = table.Column<int>(type: "int", nullable: false),
                    OutputTokens = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCallRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAccounts",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PseudonymId = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                    Role = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ContactLookupHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    PreferredLanguage = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccounts", x => x.Id);
                    table.CheckConstraint("CK_UserAccounts_Language", "[PreferredLanguage] IN ('ar','en')");
                    table.CheckConstraint("CK_UserAccounts_Role", "[Role] IN ('Patient','Therapist','Supervisor','ClinicalLead','Admin')");
                });

            migrationBuilder.CreateTable(
                name: "CareAssignments",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareAssignments_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CareAssignments_UserAccounts_TherapistUserId",
                        column: x => x.TherapistUserId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CrisisEvents",
                schema: "crisis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PseudonymId = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                    Level = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Language = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    RulePackVersion = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    OpenedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AckDeadlineUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CloseReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrisisEvents", x => x.Id);
                    table.CheckConstraint("CK_CrisisEvents_Level", "[Level] IN (3,4)");
                    table.ForeignKey(
                        name: "FK_CrisisEvents_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientKeys",
                schema: "vault",
                columns: table => new
                {
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WrappedKey = table.Column<byte[]>(type: "varbinary(1024)", maxLength: 1024, nullable: true),
                    KeyEncryptionKeyId = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ShreddedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientKeys", x => x.PatientId);
                    table.CheckConstraint("CK_PatientKeys_Shredded", "([ShreddedAtUtc] IS NULL AND [WrappedKey] IS NOT NULL) OR ([ShreddedAtUtc] IS NOT NULL AND [WrappedKey] IS NULL)");
                    table.ForeignKey(
                        name: "FK_PatientKeys_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientProfiles",
                schema: "identity",
                columns: table => new
                {
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PseudonymId = table.Column<string>(type: "varchar(11)", unicode: false, maxLength: 11, nullable: false),
                    DisplayNameCipher = table.Column<byte[]>(type: "varbinary(512)", maxLength: 512, nullable: true),
                    DateOfBirthCipher = table.Column<byte[]>(type: "varbinary(128)", maxLength: 128, nullable: true),
                    EmergencyContactCipher = table.Column<byte[]>(type: "varbinary(1024)", maxLength: 1024, nullable: true),
                    CityCode = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ShreddedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientProfiles", x => x.PatientId);
                    table.ForeignKey(
                        name: "FK_PatientProfiles_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiCallRecords_CreatedAtUtc",
                schema: "ai",
                table: "AiCallRecords",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CareAssignments_Active",
                schema: "clinical",
                table: "CareAssignments",
                columns: new[] { "TherapistUserId", "PatientId" },
                filter: "[EndedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_CareAssignments_OneActivePerPatient",
                schema: "clinical",
                table: "CareAssignments",
                column: "PatientId",
                unique: true,
                filter: "[EndedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CrisisEvents_OpenByDeadline",
                schema: "crisis",
                table: "CrisisEvents",
                columns: new[] { "Status", "AckDeadlineUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CrisisEvents_PatientId",
                schema: "crisis",
                table: "CrisisEvents",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientProfiles_PseudonymId",
                schema: "identity",
                table: "PatientProfiles",
                column: "PseudonymId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_ContactLookupHash",
                schema: "identity",
                table: "UserAccounts",
                column: "ContactLookupHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_PseudonymId",
                schema: "identity",
                table: "UserAccounts",
                column: "PseudonymId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiCallRecords",
                schema: "ai");

            migrationBuilder.DropTable(
                name: "CareAssignments",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "CrisisEvents",
                schema: "crisis");

            migrationBuilder.DropTable(
                name: "PatientKeys",
                schema: "vault");

            migrationBuilder.DropTable(
                name: "PatientProfiles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "UserAccounts",
                schema: "identity");
        }
    }
}

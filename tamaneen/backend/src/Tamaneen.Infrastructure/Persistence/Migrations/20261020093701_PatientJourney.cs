using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tamaneen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PatientJourney : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "CrisisEventNumbers",
                schema: "crisis",
                startValue: 1001L);

            migrationBuilder.AddColumn<string>(
                name: "CarePath",
                schema: "identity",
                table: "PatientProfiles",
                type: "varchar(8)",
                unicode: false,
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConsentsCompletedAtUtc",
                schema: "identity",
                table: "PatientProfiles",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ContactCipher",
                schema: "identity",
                table: "PatientProfiles",
                type: "varbinary(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactType",
                schema: "identity",
                table: "PatientProfiles",
                type: "varchar(8)",
                unicode: false,
                maxLength: 8,
                nullable: false,
                defaultValue: "phone");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SafetyInfoCompletedAtUtc",
                schema: "identity",
                table: "PatientProfiles",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ShareLocation",
                schema: "identity",
                table: "PatientProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShareLocationSinceUtc",
                schema: "identity",
                table: "PatientProfiles",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                schema: "identity",
                table: "PatientProfiles",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: false,
                defaultValue: "Asia/Riyadh");

            migrationBuilder.AddColumn<string>(
                name: "CloseOutcome",
                schema: "crisis",
                table: "CrisisEvents",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ExcerptCipher",
                schema: "crisis",
                table: "CrisisEvents",
                type: "varbinary(max)",
                maxLength: 8100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                schema: "crisis",
                table: "CrisisEvents",
                type: "int",
                nullable: false,
                defaultValueSql: "NEXT VALUE FOR [crisis].[CrisisEventNumbers]");

            migrationBuilder.CreateTable(
                name: "AuthChallenges",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<byte>(type: "tinyint", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CodeHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ContactCipher = table.Column<byte[]>(type: "varbinary(512)", maxLength: 512, nullable: false),
                    PayloadCipher = table.Column<byte[]>(type: "varbinary(2048)", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResendAvailableAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    Sends = table.Column<int>(type: "int", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthChallenges_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuthSessions",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Client = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    DeviceLabel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RefreshTokenHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    PreviousRefreshTokenHash = table.Column<byte[]>(type: "binary(32)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastActiveAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthSessions_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CareRequests",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    HandledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    HandledBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareRequests_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Consents",
                schema: "identity",
                columns: table => new
                {
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Granted = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Consents", x => new { x.PatientId, x.Key });
                    table.ForeignKey(
                        name: "FK_Consents_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Credentials",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PasswordHash = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false),
                    PasswordChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LockedUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TotpSecretCipher = table.Column<byte[]>(type: "varbinary(128)", maxLength: 128, nullable: true),
                    TotpEnabled = table.Column<bool>(type: "bit", nullable: false),
                    TotpLastStep = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Credentials", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Credentials_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrisisActions",
                schema: "crisis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrisisEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorRole = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    TextCipher = table.Column<byte[]>(type: "varbinary(max)", maxLength: 8100, nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrisisActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrisisActions_CrisisEvents_CrisisEventId",
                        column: x => x.CrisisEventId,
                        principalSchema: "crisis",
                        principalTable: "CrisisEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DataRequests",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DataRequests_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HomeworkAssignments",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TitleCipher = table.Column<byte[]>(type: "varbinary(1024)", maxLength: 1024, nullable: false),
                    InstructionsCipher = table.Column<byte[]>(type: "varbinary(max)", maxLength: 8100, nullable: false),
                    Minutes = table.Column<int>(type: "int", nullable: true),
                    Frequency = table.Column<byte>(type: "tinyint", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeworkAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HomeworkAssignments_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntakeSessions",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastActivityAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PatientTurns = table.Column<int>(type: "int", nullable: false),
                    Phq9 = table.Column<string>(type: "char(9)", nullable: false),
                    Gad7 = table.Column<string>(type: "char(7)", nullable: false),
                    SummaryCipher = table.Column<byte[]>(type: "varbinary(max)", maxLength: 16000, nullable: true),
                    SummaryByAi = table.Column<bool>(type: "bit", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TherapistApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TherapistApprovedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntakeSessions_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MoodEntries",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Tags = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    NoteCipher = table.Column<byte[]>(type: "varbinary(max)", maxLength: 8100, nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MoodEntries", x => x.Id);
                    table.CheckConstraint("CK_MoodEntries_Score", "[Score] BETWEEN 0 AND 10");
                    table.ForeignKey(
                        name: "FK_MoodEntries_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationPreferences",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Push = table.Column<bool>(type: "bit", nullable: false),
                    Sms = table.Column<bool>(type: "bit", nullable: false),
                    Email = table.Column<bool>(type: "bit", nullable: false),
                    NeutralText = table.Column<bool>(type: "bit", nullable: false),
                    QuickExit = table.Column<bool>(type: "bit", nullable: false),
                    HomeworkReminder = table.Column<byte>(type: "tinyint", nullable: false),
                    ReminderTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    QuietFrom = table.Column<TimeOnly>(type: "time", nullable: false),
                    QuietTo = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryCodes",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UsedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryCodes_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SafetyPlans",
                schema: "clinical",
                columns: table => new
                {
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SectionsCipher = table.Column<byte[]>(type: "varbinary(max)", maxLength: 16000, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedByRole = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    CoAuthorTherapistId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafetyPlans", x => x.PatientId);
                    table.ForeignKey(
                        name: "FK_SafetyPlans_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Screenings",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Instrument = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Version = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Positive = table.Column<bool>(type: "bit", nullable: false),
                    Level = table.Column<byte>(type: "tinyint", nullable: false),
                    AnswersCipher = table.Column<byte[]>(type: "varbinary(512)", maxLength: 512, nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Screenings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Screenings_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StaffProfiles",
                schema: "identity",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayNameAr = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DisplayNameEn = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OnCallTier = table.Column<byte>(type: "tinyint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_StaffProfiles_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrustedDevices",
                schema: "identity",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustedDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrustedDevices_UserAccounts_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HomeworkCheckins",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Feeling = table.Column<int>(type: "int", nullable: true),
                    NoteCipher = table.Column<byte[]>(type: "varbinary(max)", maxLength: 8100, nullable: true),
                    FeedbackCipher = table.Column<byte[]>(type: "varbinary(max)", maxLength: 8100, nullable: true),
                    FeedbackAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FeedbackSeenAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeworkCheckins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HomeworkCheckins_HomeworkAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalSchema: "clinical",
                        principalTable: "HomeworkAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IntakeMessages",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntakeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromPatient = table.Column<bool>(type: "bit", nullable: false),
                    TextCipher = table.Column<byte[]>(type: "varbinary(max)", maxLength: 20000, nullable: false),
                    IsFallback = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntakeMessages_IntakeSessions_IntakeId",
                        column: x => x.IntakeId,
                        principalSchema: "clinical",
                        principalTable: "IntakeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TriageCases",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntakeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Path = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    RiskLevel = table.Column<byte>(type: "tinyint", nullable: false),
                    Phq9 = table.Column<int>(type: "int", nullable: true),
                    Gad7 = table.Column<int>(type: "int", nullable: true),
                    CssrsPositive = table.Column<bool>(type: "bit", nullable: true),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AddendaCipher = table.Column<byte[]>(type: "varbinary(max)", maxLength: 16000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TriageCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TriageCases_IntakeSessions_IntakeId",
                        column: x => x.IntakeId,
                        principalSchema: "clinical",
                        principalTable: "IntakeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TriageCases_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TherapistProfiles",
                schema: "clinical",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TitleAr = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    TitleEn = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    BioAr = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    BioEn = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Gender = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    Specialties = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false),
                    Languages = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    LicenseVerified = table.Column<bool>(type: "bit", nullable: false),
                    AcceptingNewPatients = table.Column<bool>(type: "bit", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    SessionMinutes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TherapistProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_TherapistProfiles_StaffProfiles_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "StaffProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AvailabilitySlots",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    HoldId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HeldByPatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    HeldUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvailabilitySlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AvailabilitySlots_TherapistProfiles_TherapistUserId",
                        column: x => x.TherapistUserId,
                        principalSchema: "clinical",
                        principalTable: "TherapistProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MatchSuggestions",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    SharedSpecialties = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false),
                    Dismissed = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchSuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchSuggestions_TherapistProfiles_TherapistUserId",
                        column: x => x.TherapistUserId,
                        principalSchema: "clinical",
                        principalTable: "TherapistProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MatchSuggestions_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Appointments",
                schema: "clinical",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TherapistUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    Rescheduled = table.Column<bool>(type: "bit", nullable: false),
                    RoomId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OutcomeRecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Appointments_AvailabilitySlots_SlotId",
                        column: x => x.SlotId,
                        principalSchema: "clinical",
                        principalTable: "AvailabilitySlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_TherapistProfiles_TherapistUserId",
                        column: x => x.TherapistUserId,
                        principalSchema: "clinical",
                        principalTable: "TherapistProfiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Appointments_UserAccounts_PatientId",
                        column: x => x.PatientId,
                        principalSchema: "identity",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_PatientId_StartsAtUtc",
                schema: "clinical",
                table: "Appointments",
                columns: new[] { "PatientId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TherapistUserId_StartsAtUtc",
                schema: "clinical",
                table: "Appointments",
                columns: new[] { "TherapistUserId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_Appointments_OneConfirmedPerSlot",
                schema: "clinical",
                table: "Appointments",
                column: "SlotId",
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AuthChallenges_ExpiresAtUtc",
                schema: "identity",
                table: "AuthChallenges",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuthChallenges_UserId",
                schema: "identity",
                table: "AuthChallenges",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_PreviousRefreshTokenHash",
                schema: "identity",
                table: "AuthSessions",
                column: "PreviousRefreshTokenHash",
                filter: "[PreviousRefreshTokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_RefreshTokenHash",
                schema: "identity",
                table: "AuthSessions",
                column: "RefreshTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_UserId_RevokedAtUtc",
                schema: "identity",
                table: "AuthSessions",
                columns: new[] { "UserId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilitySlots_HoldId",
                schema: "clinical",
                table: "AvailabilitySlots",
                column: "HoldId",
                filter: "[HoldId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AvailabilitySlots_TherapistUserId_StartsAtUtc",
                schema: "clinical",
                table: "AvailabilitySlots",
                columns: new[] { "TherapistUserId", "StartsAtUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CareRequests_HandledAtUtc_RequestedAtUtc",
                schema: "clinical",
                table: "CareRequests",
                columns: new[] { "HandledAtUtc", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CareRequests_PatientId",
                schema: "clinical",
                table: "CareRequests",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_CrisisActions_CrisisEventId_OccurredAtUtc",
                schema: "crisis",
                table: "CrisisActions",
                columns: new[] { "CrisisEventId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DataRequests_PatientId_Kind",
                schema: "identity",
                table: "DataRequests",
                columns: new[] { "PatientId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_HomeworkAssignments_PatientId_EndsOn",
                schema: "clinical",
                table: "HomeworkAssignments",
                columns: new[] { "PatientId", "EndsOn" });

            migrationBuilder.CreateIndex(
                name: "IX_HomeworkCheckins_AssignmentId_Day",
                schema: "clinical",
                table: "HomeworkCheckins",
                columns: new[] { "AssignmentId", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HomeworkCheckins_PatientId",
                schema: "clinical",
                table: "HomeworkCheckins",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeMessages_IntakeId_CreatedAtUtc",
                schema: "clinical",
                table: "IntakeMessages",
                columns: new[] { "IntakeId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeSessions_PatientId_StartedAtUtc",
                schema: "clinical",
                table: "IntakeSessions",
                columns: new[] { "PatientId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchSuggestions_PatientId_TherapistUserId",
                schema: "clinical",
                table: "MatchSuggestions",
                columns: new[] { "PatientId", "TherapistUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchSuggestions_TherapistUserId",
                schema: "clinical",
                table: "MatchSuggestions",
                column: "TherapistUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MoodEntries_PatientId_RecordedAtUtc",
                schema: "clinical",
                table: "MoodEntries",
                columns: new[] { "PatientId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryCodes_UserId_CodeHash",
                schema: "identity",
                table: "RecoveryCodes",
                columns: new[] { "UserId", "CodeHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Screenings_PatientId_CompletedAtUtc",
                schema: "clinical",
                table: "Screenings",
                columns: new[] { "PatientId", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TriageCases_IntakeId",
                schema: "clinical",
                table: "TriageCases",
                column: "IntakeId");

            migrationBuilder.CreateIndex(
                name: "IX_TriageCases_PatientId",
                schema: "clinical",
                table: "TriageCases",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_TriageCases_Status_DueAtUtc",
                schema: "clinical",
                table: "TriageCases",
                columns: new[] { "Status", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustedDevices_TokenHash",
                schema: "identity",
                table: "TrustedDevices",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrustedDevices_UserId",
                schema: "identity",
                table: "TrustedDevices",
                column: "UserId");

            // Row-level security for the patient-journey tables. Appointments are also visible to the
            // therapist of the session (before any care assignment exists). The scheduling service may
            // create the care assignment that a confirmed booking implies, and nothing else.
            migrationBuilder.Sql(@"
CREATE FUNCTION [security].[fn_appointment_access](@PatientId uniqueidentifier, @TherapistUserId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) IN (N'Supervisor', N'ClinicalLead', N'CrisisService')
       OR (CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'Patient'
           AND @PatientId = CAST(SESSION_CONTEXT(N'patient_id') AS uniqueidentifier))
       OR (CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'Therapist'
           AND @TherapistUserId = CAST(SESSION_CONTEXT(N'user_id') AS uniqueidentifier));");

            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [identity].[Consents],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [identity].[Consents] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [identity].[Consents] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [identity].[Consents] BEFORE DELETE,
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [identity].[DataRequests],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [identity].[DataRequests] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [identity].[DataRequests] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [identity].[DataRequests] BEFORE DELETE,
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[IntakeSessions],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[IntakeSessions] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[IntakeSessions] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[IntakeSessions] BEFORE DELETE,
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[IntakeMessages],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[IntakeMessages] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[IntakeMessages] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[IntakeMessages] BEFORE DELETE,
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[TriageCases],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[TriageCases] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[TriageCases] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[TriageCases] BEFORE DELETE,
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[Screenings],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[Screenings] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[Screenings] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[Screenings] BEFORE DELETE,
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[MoodEntries],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[MoodEntries] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[MoodEntries] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[MoodEntries] BEFORE DELETE,
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[HomeworkAssignments],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[HomeworkAssignments] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[HomeworkAssignments] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[HomeworkAssignments] BEFORE DELETE,
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[HomeworkCheckins],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[HomeworkCheckins] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[HomeworkCheckins] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[HomeworkCheckins] BEFORE DELETE,
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[SafetyPlans],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[SafetyPlans] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[SafetyPlans] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[SafetyPlans] BEFORE DELETE,
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[CareRequests],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[CareRequests] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[CareRequests] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[CareRequests] BEFORE DELETE,
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[MatchSuggestions],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[MatchSuggestions] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[MatchSuggestions] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [clinical].[MatchSuggestions] BEFORE DELETE,
    ADD FILTER PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[CrisisActions],
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[CrisisActions] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[CrisisActions] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_patient_access]([PatientId]) ON [crisis].[CrisisActions] BEFORE DELETE,
    ADD FILTER PREDICATE [security].[fn_appointment_access]([PatientId], [TherapistUserId]) ON [clinical].[Appointments],
    ADD BLOCK  PREDICATE [security].[fn_appointment_access]([PatientId], [TherapistUserId]) ON [clinical].[Appointments] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_appointment_access]([PatientId], [TherapistUserId]) ON [clinical].[Appointments] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_appointment_access]([PatientId], [TherapistUserId]) ON [clinical].[Appointments] BEFORE DELETE;");

            // Functions bound to the policy cannot change while predicates use them: detach, alter, re-attach.
            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    DROP FILTER PREDICATE ON [clinical].[CareAssignments],
    DROP BLOCK PREDICATE ON [clinical].[CareAssignments] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[CareAssignments] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[CareAssignments] BEFORE DELETE;");
            migrationBuilder.Sql(@"
ALTER FUNCTION [security].[fn_assignment_read](@PatientId uniqueidentifier, @TherapistUserId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) IN (N'Supervisor', N'ClinicalLead', N'CrisisService', N'SchedulingService')
       OR (CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'Therapist'
           AND @TherapistUserId = CAST(SESSION_CONTEXT(N'user_id') AS uniqueidentifier))
       OR (CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) = N'Patient'
           AND @PatientId = CAST(SESSION_CONTEXT(N'patient_id') AS uniqueidentifier));");
            migrationBuilder.Sql(@"
ALTER FUNCTION [security].[fn_assignment_write](@PatientId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) IN (N'Supervisor', N'ClinicalLead', N'SchedulingService');");
            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    ADD FILTER PREDICATE [security].[fn_assignment_read]([PatientId], [TherapistUserId]) ON [clinical].[CareAssignments],
    ADD BLOCK  PREDICATE [security].[fn_assignment_write]([PatientId]) ON [clinical].[CareAssignments] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_assignment_write]([PatientId]) ON [clinical].[CareAssignments] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_assignment_write]([PatientId]) ON [clinical].[CareAssignments] BEFORE DELETE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            // Functions bound to the policy cannot change while predicates use them: detach, alter, re-attach.
            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    DROP FILTER PREDICATE ON [clinical].[CareAssignments],
    DROP BLOCK PREDICATE ON [clinical].[CareAssignments] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[CareAssignments] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[CareAssignments] BEFORE DELETE;");
            migrationBuilder.Sql(@"
ALTER FUNCTION [security].[fn_assignment_read](@PatientId uniqueidentifier, @TherapistUserId uniqueidentifier)
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
            migrationBuilder.Sql(@"
ALTER FUNCTION [security].[fn_assignment_write](@PatientId uniqueidentifier)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [allowed]
    WHERE CAST(SESSION_CONTEXT(N'role') AS nvarchar(32)) IN (N'Supervisor', N'ClinicalLead');");
            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    ADD FILTER PREDICATE [security].[fn_assignment_read]([PatientId], [TherapistUserId]) ON [clinical].[CareAssignments],
    ADD BLOCK  PREDICATE [security].[fn_assignment_write]([PatientId]) ON [clinical].[CareAssignments] AFTER INSERT,
    ADD BLOCK  PREDICATE [security].[fn_assignment_write]([PatientId]) ON [clinical].[CareAssignments] AFTER UPDATE,
    ADD BLOCK  PREDICATE [security].[fn_assignment_write]([PatientId]) ON [clinical].[CareAssignments] BEFORE DELETE;");

            migrationBuilder.Sql(@"
ALTER SECURITY POLICY [security].[PatientDataPolicy]
    DROP FILTER PREDICATE ON [identity].[Consents],
    DROP BLOCK PREDICATE ON [identity].[Consents] AFTER INSERT,
    DROP BLOCK PREDICATE ON [identity].[Consents] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [identity].[Consents] BEFORE DELETE,
    DROP FILTER PREDICATE ON [identity].[DataRequests],
    DROP BLOCK PREDICATE ON [identity].[DataRequests] AFTER INSERT,
    DROP BLOCK PREDICATE ON [identity].[DataRequests] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [identity].[DataRequests] BEFORE DELETE,
    DROP FILTER PREDICATE ON [clinical].[IntakeSessions],
    DROP BLOCK PREDICATE ON [clinical].[IntakeSessions] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[IntakeSessions] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[IntakeSessions] BEFORE DELETE,
    DROP FILTER PREDICATE ON [clinical].[IntakeMessages],
    DROP BLOCK PREDICATE ON [clinical].[IntakeMessages] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[IntakeMessages] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[IntakeMessages] BEFORE DELETE,
    DROP FILTER PREDICATE ON [clinical].[TriageCases],
    DROP BLOCK PREDICATE ON [clinical].[TriageCases] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[TriageCases] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[TriageCases] BEFORE DELETE,
    DROP FILTER PREDICATE ON [clinical].[Screenings],
    DROP BLOCK PREDICATE ON [clinical].[Screenings] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[Screenings] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[Screenings] BEFORE DELETE,
    DROP FILTER PREDICATE ON [clinical].[MoodEntries],
    DROP BLOCK PREDICATE ON [clinical].[MoodEntries] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[MoodEntries] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[MoodEntries] BEFORE DELETE,
    DROP FILTER PREDICATE ON [clinical].[HomeworkAssignments],
    DROP BLOCK PREDICATE ON [clinical].[HomeworkAssignments] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[HomeworkAssignments] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[HomeworkAssignments] BEFORE DELETE,
    DROP FILTER PREDICATE ON [clinical].[HomeworkCheckins],
    DROP BLOCK PREDICATE ON [clinical].[HomeworkCheckins] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[HomeworkCheckins] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[HomeworkCheckins] BEFORE DELETE,
    DROP FILTER PREDICATE ON [clinical].[SafetyPlans],
    DROP BLOCK PREDICATE ON [clinical].[SafetyPlans] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[SafetyPlans] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[SafetyPlans] BEFORE DELETE,
    DROP FILTER PREDICATE ON [clinical].[CareRequests],
    DROP BLOCK PREDICATE ON [clinical].[CareRequests] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[CareRequests] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[CareRequests] BEFORE DELETE,
    DROP FILTER PREDICATE ON [clinical].[MatchSuggestions],
    DROP BLOCK PREDICATE ON [clinical].[MatchSuggestions] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[MatchSuggestions] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[MatchSuggestions] BEFORE DELETE,
    DROP FILTER PREDICATE ON [crisis].[CrisisActions],
    DROP BLOCK PREDICATE ON [crisis].[CrisisActions] AFTER INSERT,
    DROP BLOCK PREDICATE ON [crisis].[CrisisActions] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [crisis].[CrisisActions] BEFORE DELETE,
    DROP FILTER PREDICATE ON [clinical].[Appointments],
    DROP BLOCK PREDICATE ON [clinical].[Appointments] AFTER INSERT,
    DROP BLOCK PREDICATE ON [clinical].[Appointments] AFTER UPDATE,
    DROP BLOCK PREDICATE ON [clinical].[Appointments] BEFORE DELETE;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS [security].[fn_appointment_access];");

            migrationBuilder.DropTable(
                name: "Appointments",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "AuthChallenges",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "AuthSessions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "CareRequests",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "Consents",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "Credentials",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "CrisisActions",
                schema: "crisis");

            migrationBuilder.DropTable(
                name: "DataRequests",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "HomeworkCheckins",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "IntakeMessages",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "MatchSuggestions",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "MoodEntries",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "NotificationPreferences",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "RecoveryCodes",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "SafetyPlans",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "Screenings",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "TriageCases",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "TrustedDevices",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "AvailabilitySlots",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "HomeworkAssignments",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "IntakeSessions",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "TherapistProfiles",
                schema: "clinical");

            migrationBuilder.DropTable(
                name: "StaffProfiles",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "CarePath",
                schema: "identity",
                table: "PatientProfiles");

            migrationBuilder.DropColumn(
                name: "ConsentsCompletedAtUtc",
                schema: "identity",
                table: "PatientProfiles");

            migrationBuilder.DropColumn(
                name: "ContactCipher",
                schema: "identity",
                table: "PatientProfiles");

            migrationBuilder.DropColumn(
                name: "ContactType",
                schema: "identity",
                table: "PatientProfiles");

            migrationBuilder.DropColumn(
                name: "SafetyInfoCompletedAtUtc",
                schema: "identity",
                table: "PatientProfiles");

            migrationBuilder.DropColumn(
                name: "ShareLocation",
                schema: "identity",
                table: "PatientProfiles");

            migrationBuilder.DropColumn(
                name: "ShareLocationSinceUtc",
                schema: "identity",
                table: "PatientProfiles");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                schema: "identity",
                table: "PatientProfiles");

            migrationBuilder.DropColumn(
                name: "CloseOutcome",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "ExcerptCipher",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropColumn(
                name: "Number",
                schema: "crisis",
                table: "CrisisEvents");

            migrationBuilder.DropSequence(
                name: "CrisisEventNumbers",
                schema: "crisis");
        }
    }
}
